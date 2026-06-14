namespace CatalogService.Domain;

public sealed class BranchService
{
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ServiceId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
