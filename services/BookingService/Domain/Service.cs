namespace BookingService.Domain;

public sealed class Service
{
    public Guid ServiceId { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int DurationMinutes { get; set; }
    public decimal? ReferencePrice { get; set; }
    public string Modality { get; set; } = "presencial";
    public bool RequiresConfirmation { get; set; }
    public string Status { get; set; } = "active";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
