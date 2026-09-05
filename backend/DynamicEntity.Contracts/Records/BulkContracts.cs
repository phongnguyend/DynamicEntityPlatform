using System.Text.Json;

namespace DynamicEntity.Contracts.Records;

public sealed record BulkPatchRequest(IReadOnlyList<Guid> RecordIds, JsonElement Data);
public sealed record BulkDeleteRequest(IReadOnlyList<Guid> RecordIds);
public sealed record BulkResultResponse(int AffectedRows);
public sealed record MergeRequest(Guid MatchFieldId, IReadOnlyList<JsonElement> Records);
public sealed record MergeResultResponse(int InsertedRows, int UpdatedRows);
