using System.Text;
using System.Text.Json;
using DynamicEntity.Application.Abstractions;
using DynamicEntity.Application.Common;

namespace DynamicEntity.Application.Records;

public sealed class RecordExportService(
    IControlPlaneStore controlPlane,
    IEntityMetadataStore entities,
    IFieldMetadataStore fields,
    RecordService records)
{
    public async Task ExportCsvAsync(Guid tenantId, Guid entityId, Stream output, CancellationToken cancellationToken)
    {
        var storage = await new TenantStorageGuard(controlPlane).RequireActiveAsync(tenantId, cancellationToken);
        _ = await entities.GetAsync(tenantId, entityId, storage, cancellationToken)
            ?? throw new NotFoundException($"Entity '{entityId}' was not found.");
        var definitions = await fields.ListAsync(tenantId, entityId, storage, cancellationToken);
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync(string.Join(',', new[] { "Id" }.Concat(definitions.Select(field => Escape(field.DisplayName)))));
        string? cursor = null;
        do
        {
            var page = await records.ListAsync(tenantId, entityId, 200, cursor, cancellationToken);
            foreach (var record in page.Items)
            {
                using var data = JsonDocument.Parse(record.Data);
                var values = definitions.Select(field => data.RootElement.TryGetProperty(field.StorageKey, out var value)
                    ? Escape(Value(value)) : string.Empty);
                await writer.WriteLineAsync($"{record.Id:D},{string.Join(',', values)}".AsMemory(), cancellationToken);
            }
            await writer.FlushAsync(cancellationToken);
            cursor = page.NextCursor;
        } while (cursor is not null);
    }

    private static string Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => string.Empty,
        JsonValueKind.String => value.GetString() ?? string.Empty,
        _ => value.GetRawText()
    };
    private static string Escape(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : value;
}
