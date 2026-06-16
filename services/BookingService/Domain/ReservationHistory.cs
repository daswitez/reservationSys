namespace BookingService.Domain;

public sealed class ReservationHistory
{
    public Guid HistoryId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ReservationId { get; set; }
    public Guid? UserId { get; set; }
    public string? PreviousStatus { get; set; }
    public string NewStatus { get; set; } = "CONFIRMED";
    public string Action { get; set; } = "CREATED";
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
