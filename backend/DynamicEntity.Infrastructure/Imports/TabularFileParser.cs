using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;

namespace DynamicEntity.Infrastructure.Imports;

public sealed class TabularFileParser : ITabularFileParser
{
    public async Task ParseAsync(
        Stream stream,
        string fileName,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask> onHeader,
        Func<long, IReadOnlyDictionary<string, string?>, CancellationToken, ValueTask> onRow,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            await ParseCsvAsync(stream, onHeader, onRow, cancellationToken);
        else if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            await ParseXlsxAsync(stream, onHeader, onRow, cancellationToken);
        else
            throw new ValidationException("Only .csv and .xlsx files are supported.");
    }

    private static async Task ParseCsvAsync(
        Stream stream,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask> onHeader,
        Func<long, IReadOnlyDictionary<string, string?>, CancellationToken, ValueTask> onRow,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, leaveOpen: true);
        var first = await ReadCsvRecordAsync(reader, cancellationToken)
            ?? throw new ValidationException("The CSV file is empty.");
        var headers = ParseCsvLine(first).Select((value, index) => UniqueHeader(value, index)).ToArray();
        ValidateHeaders(headers);
        await onHeader(headers, cancellationToken);
        long rowNumber = 1;
        while (await ReadCsvRecordAsync(reader, cancellationToken) is { } line)
        {
            rowNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            var values = ParseCsvLine(line);
            var row = headers.Select((header, index) => new { header, value = index < values.Count ? values[index] : null })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            await onRow(rowNumber, row, cancellationToken);
        }
    }

    private static async Task ParseXlsxAsync(
        Stream stream,
        Func<IReadOnlyList<string>, CancellationToken, ValueTask> onHeader,
        Func<long, IReadOnlyDictionary<string, string?>, CancellationToken, ValueTask> onRow,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(archive);
        var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")
            ?? throw new ValidationException("The workbook does not contain a first worksheet.");
        using var sheetStream = sheet.Open();
        using var xml = XmlReader.Create(sheetStream, new XmlReaderSettings { IgnoreWhitespace = true });
        string[]? headers = null;
        long logicalRow = 0;
        while (xml.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (xml.NodeType != XmlNodeType.Element || xml.LocalName != "row") continue;
            logicalRow++;
            var cells = ReadRow(xml.ReadSubtree(), shared);
            if (headers is null)
            {
                var width = cells.Count == 0 ? 0 : cells.Keys.Max() + 1;
                headers = Enumerable.Range(0, width).Select(index => UniqueHeader(cells.GetValueOrDefault(index), index)).ToArray();
                ValidateHeaders(headers);
                await onHeader(headers, cancellationToken);
                continue;
            }
            var row = headers.Select((header, index) => new { header, value = cells.GetValueOrDefault(index) })
                .ToDictionary(item => item.header, item => item.value, StringComparer.OrdinalIgnoreCase);
            await onRow(logicalRow, row, cancellationToken);
        }
        if (headers is null) throw new ValidationException("The workbook is empty.");
    }

    private static Dictionary<int, string?> ReadRow(XmlReader rowReader, IReadOnlyList<string> shared)
    {
        var cells = new Dictionary<int, string?>();
        while (rowReader.Read())
        {
            if (rowReader.NodeType != XmlNodeType.Element || rowReader.LocalName != "c") continue;
            var reference = rowReader.GetAttribute("r") ?? "A1";
            var type = rowReader.GetAttribute("t");
            using var cellReader = rowReader.ReadSubtree();
            string? value = null;
            while (cellReader.Read())
            {
                if (cellReader.NodeType == XmlNodeType.Element && cellReader.LocalName is "v" or "t")
                    value = cellReader.ReadElementContentAsString();
            }
            if (type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count) value = shared[index];
            cells[ColumnIndex(reference)] = value;
        }
        return cells;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        return document.Descendants().Where(element => element.Name.LocalName == "si")
            .Select(item => string.Concat(item.Descendants().Where(element => element.Name.LocalName == "t").Select(element => element.Value)))
            .ToArray();
    }

    private static List<string?> ParseCsvLine(string line)
    {
        var values = new List<string?>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(character);
        }
        if (quoted) throw new ValidationException("A CSV row contains an unterminated quoted value.");
        values.Add(value.ToString());
        return values;
    }

    private static async Task<string?> ReadCsvRecordAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return null;
        var record = new StringBuilder(line);
        while (HasOpenQuote(record))
        {
            var continuation = await reader.ReadLineAsync(cancellationToken)
                ?? throw new ValidationException("The CSV file ends inside a quoted value.");
            record.Append('\n').Append(continuation);
        }
        return record.ToString();
    }

    private static bool HasOpenQuote(StringBuilder value)
    {
        var quoted = false;
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '"') continue;
            if (quoted && index + 1 < value.Length && value[index + 1] == '"') index++;
            else quoted = !quoted;
        }
        return quoted;
    }

    private static int ColumnIndex(string reference)
    {
        var result = 0;
        foreach (var character in reference.TakeWhile(char.IsLetter)) result = result * 26 + char.ToUpperInvariant(character) - 'A' + 1;
        return result - 1;
    }

    private static string UniqueHeader(string? value, int index) => string.IsNullOrWhiteSpace(value) ? $"Column{index + 1}" : value.Trim();
    private static void ValidateHeaders(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0) throw new ValidationException("The file has no columns.");
        if (headers.Count != headers.Distinct(StringComparer.OrdinalIgnoreCase).Count())
            throw new ValidationException("Source column names must be unique.");
    }
}
