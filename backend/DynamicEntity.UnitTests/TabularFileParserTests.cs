using System.IO.Compression;
using System.Text;
using DynamicEntity.Application.Common;
using DynamicEntity.Infrastructure.Imports;

namespace DynamicEntity.UnitTests;

public sealed class TabularFileParserTests
{
    [Fact]
    public async Task Csv_StreamsHeaderAndEscapedValues()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Note\r\nAda,\"Hello, world\"\r\n"));
        IReadOnlyList<string>? headers = null;
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        await new TabularFileParser().ParseAsync(stream, "people.csv",
            (value, _) => { headers = value; return ValueTask.CompletedTask; },
            (_, value, _) => { rows.Add(value); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(["Name", "Note"], headers);
        Assert.Equal("Hello, world", rows[0]["Note"]);
    }

    [Fact]
    public async Task Xlsx_StreamsFirstWorksheetAndSharedStrings()
    {
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, "xl/sharedStrings.xml", """<sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si><t>Name</t></si><si><t>Ada</t></si></sst>""");
            Write(archive, "xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="inlineStr"><is><t>Age</t></is></c></row><row r="2"><c r="A2" t="s"><v>1</v></c><c r="B2"><v>36</v></c></row></sheetData></worksheet>""");
        }
        stream.Position = 0;
        IReadOnlyList<string>? headers = null;
        var rows = new List<IReadOnlyDictionary<string, string?>>();

        await new TabularFileParser().ParseAsync(stream, "people.xlsx",
            (value, _) => { headers = value; return ValueTask.CompletedTask; },
            (_, value, _) => { rows.Add(value); return ValueTask.CompletedTask; }, CancellationToken.None);

        Assert.Equal(["Name", "Age"], headers);
        Assert.Equal("Ada", rows[0]["Name"]);
        Assert.Equal("36", rows[0]["Age"]);
    }

    [Fact]
    public async Task RejectsDuplicateHeaders()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,name\nAda,Lovelace"));
        await Assert.ThrowsAsync<ValidationException>(() => new TabularFileParser().ParseAsync(stream, "people.csv",
            (_, _) => ValueTask.CompletedTask, (_, _, _) => ValueTask.CompletedTask, CancellationToken.None));
    }

    [Fact]
    public async Task Csv_AllowsNewlinesInsideQuotedValues()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Note\nAda,\"Line one\nLine two\"\n"));
        var rows = new List<IReadOnlyDictionary<string, string?>>();
        await new TabularFileParser().ParseAsync(stream, "people.csv", (_, _) => ValueTask.CompletedTask,
            (_, row, _) => { rows.Add(row); return ValueTask.CompletedTask; }, CancellationToken.None);
        Assert.Equal("Line one\nLine two", rows[0]["Note"]);
    }

    private static void Write(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
