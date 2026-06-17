namespace ReportingService.Features.Reports;

public sealed record ServiceRankingResponse(
    string PeriodFrom,
    string PeriodTo,
    IReadOnlyList<ServiceSummaryItem> Services);

public sealed record ServiceSummaryItem(
    int Rank,
    Guid ServiceId,
    string ServiceName,
    int TotalCreated,
    int TotalCancelled,
    int TotalAttended,
    int TotalNoShow,
    int TotalReservedMinutes);
