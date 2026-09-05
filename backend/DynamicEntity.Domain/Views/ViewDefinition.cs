namespace DynamicEntity.Domain.Views;

public sealed record ViewDefinition(
    Guid Id,
    Guid EntityId,
    string Name,
    string DefinitionJson,
    Guid? CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
