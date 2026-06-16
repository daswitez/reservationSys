namespace CatalogService.Features.ServiceResources;

public sealed record ServiceResourceResponse(
    Guid TenantId,
    Guid ServiceId,
    Guid ResourceId,
    bool Required,
    int Priority,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record CompatibleResourceResponse(
    Guid ResourceId,
    Guid TenantId,
    Guid BranchId,
    string Name,
    string ResourceType,
    string? Description,
    int Capacity,
    bool Required,
    int Priority);
