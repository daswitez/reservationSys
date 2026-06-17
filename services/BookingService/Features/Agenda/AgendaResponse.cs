namespace BookingService.Features.Agenda;

public sealed record AgendaResponse(
    DateOnly Date,
    Guid BranchId,
    IReadOnlyList<AgendaReservationItem> Reservations,
    IReadOnlyList<AgendaBlockItem> Blocks);

public sealed record AgendaReservationItem(
    Guid ReservationId,
    Guid ResourceId,
    Guid ServiceId,
    Guid ClientUserId,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Notes);

public sealed record AgendaBlockItem(
    Guid BlockId,
    Guid ResourceId,
    string? Reason,
    string BlockType,
    string Status,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);
