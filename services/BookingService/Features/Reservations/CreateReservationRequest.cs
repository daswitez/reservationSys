namespace BookingService.Features.Reservations;

public sealed record CreateReservationRequest(
    Guid BranchId,
    Guid ServiceId,
    Guid? ResourceId,
    DateTimeOffset StartAt,
    string? Notes);
