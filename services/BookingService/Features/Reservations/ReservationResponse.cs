namespace BookingService.Features.Reservations;

public sealed record ReservationResponse(
    Guid ReservationId,
    Guid TenantId,
    Guid BranchId,
    Guid ServiceId,
    Guid ResourceId,
    Guid ClientUserId,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Notes,
    DateTimeOffset CreatedAt);
