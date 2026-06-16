namespace CatalogService.Features.Resources;

public sealed record ResourceResponse(
    Guid ResourceId,
    Guid TenantId,
    Guid BranchId,
    string Name,
    string ResourceType,
    string? Description,
    int Capacity,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
