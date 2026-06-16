namespace BookingService.Domain;

public sealed class ResourceBlock
{
    public Guid BlockId { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public Guid ResourceId { get; set; }
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public string? Reason { get; set; }
    public string BlockType { get; set; } = "manual";
    public string Status { get; set; } = "ACTIVE";
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
