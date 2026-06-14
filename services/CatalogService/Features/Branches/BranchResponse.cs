namespace CatalogService.Features.Branches;

public sealed record BranchResponse(
    Guid BranchId,
    Guid TenantId,
    string Name,
    string Address,
    string Phone,
    string? EmailContact,
    string Timezone,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
