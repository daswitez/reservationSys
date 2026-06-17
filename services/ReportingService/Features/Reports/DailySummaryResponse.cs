namespace ReportingService.Features.Reports;

public sealed record DailySummaryResponse(
    Guid TenantId,
    Guid? BranchId,
    string? BranchName,
    string Date,
    int TotalCreated,
    int TotalConfirmed,
    int TotalCancelled,
    int TotalAttended,
    int TotalNoShow,
    int TotalReservedMinutes,
    DateTimeOffset? UpdatedAt,
    string DataStatus);
