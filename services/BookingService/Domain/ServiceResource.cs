namespace BookingService.Domain;

public sealed class ServiceResource
{
    public Guid TenantId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ResourceId { get; set; }
    public bool Required { get; set; } = true;
    public int Priority { get; set; } = 1;
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
}
