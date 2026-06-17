namespace ReportingService.Features.Reports;

public sealed record PeakHoursResponse(
    Guid BranchId,
    string PeriodFrom,
    string PeriodTo,
    IReadOnlyList<PeakHourItem> Hours);

public sealed record PeakHourItem(
    int HourOfDay,
    int TotalCreated,
    int TotalAttended,
    int TotalCancelled);
