namespace CatalogService.Features.ResourceSchedules;

public sealed record ResourceScheduleResponse(
    Guid ScheduleId,
    Guid TenantId,
    Guid BranchId,
    Guid ResourceId,
    short DayOfWeek,
    string StartTime,
    string EndTime,
    string? ValidFrom,
    string? ValidTo,
    string Status,
    DateTimeOffset CreatedAt);
