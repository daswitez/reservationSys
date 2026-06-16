namespace BookingService.Domain;

public sealed class CatalogTenant
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? MainCategory { get; set; }
    public string Timezone { get; set; } = "America/La_Paz";
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
