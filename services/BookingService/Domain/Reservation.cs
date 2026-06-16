namespace BookingService.Domain;

public sealed class Reservation
{
    public Guid ReservationId { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ClientUserId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid ResourceId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public string Status { get; set; } = "CONFIRMED";
    public string ChannelOrigin { get; set; } = "web";
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
