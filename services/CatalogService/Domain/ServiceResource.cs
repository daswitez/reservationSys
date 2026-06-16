namespace CatalogService.Domain;

public sealed class ServiceResource
{
    public Guid TenantId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ResourceId { get; set; }
    public bool Required { get; set; }
    public int Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
