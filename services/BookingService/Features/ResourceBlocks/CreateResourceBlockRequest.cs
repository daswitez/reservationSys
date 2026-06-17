namespace BookingService.Features.ResourceBlocks;

public sealed record CreateResourceBlockRequest(
    Guid ResourceId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    string? BlockType);
