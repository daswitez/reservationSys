namespace CatalogService.Domain;

public sealed class Service
{
    public Guid ServiceId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public decimal? ReferencePrice { get; set; }
    public string Modality { get; set; } = string.Empty;
    public bool RequiresConfirmation { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
