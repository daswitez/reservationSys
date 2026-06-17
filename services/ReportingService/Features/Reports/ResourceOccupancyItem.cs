namespace ReportingService.Features.Reports;

public sealed record ResourceOccupancyItem(
    Guid ResourceId,
    string ResourceName,
    string ResourceType,
    string Date,
    int TotalReservations,
    int TotalAttended,
    int TotalCancelled,
    int TotalNoShow,
    int ReservedMinutes,
    int BlockedMinutes,
    DateTimeOffset? UpdatedAt);
