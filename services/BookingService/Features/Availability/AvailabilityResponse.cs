namespace BookingService.Features.Availability;

public sealed record AvailabilityResponse(
    Guid BranchId,
    Guid ServiceId,
    string Date,
    int SlotMinutes,
    IReadOnlyList<AvailableSlotResponse> AvailableSlots);

public sealed record AvailableSlotResponse(
    Guid ResourceId,
    string ResourceName,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt);
