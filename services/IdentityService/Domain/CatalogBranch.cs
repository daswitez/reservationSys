namespace IdentityService.Domain;

public sealed class CatalogBranch
{
    public Guid BranchId { get; set; }
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? EmailContact { get; set; }
    public required string Timezone { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
