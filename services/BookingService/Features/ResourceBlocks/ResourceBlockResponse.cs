namespace BookingService.Features.ResourceBlocks;

public sealed record ResourceBlockResponse(
    Guid BlockId,
    Guid TenantId,
    Guid BranchId,
    Guid ResourceId,
    DateTimeOffset StartAt,
    DateTimeOffset EndAt,
    string? Reason,
    string BlockType,
    string Status,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt);
