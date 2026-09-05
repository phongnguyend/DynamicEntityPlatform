using System.Text.Json;

namespace DynamicEntity.Contracts.Records;

public sealed record CreateRecordRequest(JsonElement Data);

public sealed record UpdateRecordRequest(JsonElement Data, string ExpectedVersion);

public sealed record RecordResponse(
    Guid Id,
    JsonElement Data,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy,
    DateTimeOffset UpdatedAt,
    Guid? UpdatedBy,
    string Version);

public sealed record RecordPageResponse(IReadOnlyList<RecordResponse> Items, string? NextCursor);
