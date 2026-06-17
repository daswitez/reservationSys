namespace BookingService.Features.Reservations;

public sealed record ReservationSearchItem(
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
    DateTimeOffset CreatedAt,
    IReadOnlyList<ReservationHistoryItem> History);

public sealed record ReservationHistoryItem(
    string Action,
    string? PreviousStatus,
    string? NewStatus,
    string? Reason,
    Guid? UserId,
    DateTimeOffset CreatedAt);
