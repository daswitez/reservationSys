using System.Security.Claims;
using Cassandra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ReportingService.Common;
using ReportingService.Features.Reports;

namespace ReportingService.Controllers;

[ApiController]
[Route("reports")]
public sealed class ReportsController(Cassandra.ISession session) : ControllerBase
{
    [HttpGet("daily-summary")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<IActionResult> GetDailySummary(
        [FromQuery] string? date,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(date) ||
            !DateOnly.TryParseExact(date, "yyyy-MM-dd", out var parsedDate))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "date es requerido y debe tener formato yyyy-MM-dd."));
        }

        var role = User.FindFirstValue("roles") ?? string.Empty;

        if (role == "client")
            return Forbid();

        Guid resolvedTenantId;
        if (role == "super_admin")
        {
            if (!tenantId.HasValue)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "super_admin debe especificar tenantId."));
            }
            resolvedTenantId = tenantId.Value;
        }
        else
        {
            var tenantClaim = User.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantClaim, out resolvedTenantId))
                return Forbid();
        }

        // branch_admin: if branchId not provided, default to claim; if provided, must match claim
        if (role == "branch_admin")
        {
            var claimedBranchStr = User.FindFirstValue("branch_id");
            if (!Guid.TryParse(claimedBranchStr, out var claimedBranch))
                return Forbid();

            if (!branchId.HasValue)
                branchId = claimedBranch;
            else if (branchId.Value != claimedBranch)
                return Forbid();
        }

        var summary = branchId.HasValue
            ? await GetBranchSummaryAsync(resolvedTenantId, branchId.Value, parsedDate)
            : await GetTenantSummaryAsync(resolvedTenantId, parsedDate);

        return Ok(ApiResponse<DailySummaryResponse>.Ok(summary));
    }

    // ── HU-025 ───────────────────────────────────────────────────────────────

    [HttpGet("resources/occupancy")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<IActionResult> GetResourceOccupancy(
        [FromQuery] Guid? branchId = null,
        [FromQuery] string? date = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var role = User.FindFirstValue("roles") ?? string.Empty;
        if (role == "client") return Forbid();

        if (!branchId.HasValue)
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "branchId es requerido."));
        }

        Guid resolvedTenantId;
        if (role == "super_admin")
        {
            if (!tenantId.HasValue)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "super_admin debe especificar tenantId."));
            }
            resolvedTenantId = tenantId.Value;
        }
        else
        {
            if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out resolvedTenantId))
                return Forbid();
        }

        if (role == "branch_admin")
        {
            if (!Guid.TryParse(User.FindFirstValue("branch_id"), out var claimedBranch)
                || branchId.Value != claimedBranch)
                return Forbid();
        }

        // Resolve date range
        DateOnly from;
        DateOnly to;

        if (date is not null)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "date debe tener formato yyyy-MM-dd."));
            }
            to = from;
        }
        else if (dateFrom is not null || dateTo is not null)
        {
            from = DateOnly.FromDateTime(DateTime.UtcNow);
            to = from;

            if (dateFrom is not null && !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateFrom debe tener formato yyyy-MM-dd."));
            }

            if (dateTo is not null && !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out to))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateTo debe tener formato yyyy-MM-dd."));
            }

            if (to < from)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateTo debe ser mayor o igual a dateFrom."));
            }

            if (to.DayNumber - from.DayNumber > 30)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "El rango de fechas no puede exceder 31 días."));
            }
        }
        else
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "Se requiere date, dateFrom o dateTo."));
        }

        var items = new List<ResourceOccupancyItem>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var dayItems = await QueryOccupancyForDateAsync(
                resolvedTenantId, branchId.Value, d);
            items.AddRange(dayItems);
        }

        return Ok(ApiResponse<IReadOnlyList<ResourceOccupancyItem>>.Ok(items));
    }

    private async Task<IReadOnlyList<ResourceOccupancyItem>> QueryOccupancyForDateAsync(
        Guid tenantId, Guid branchId, DateOnly date)
    {
        const string cql = """
            SELECT resource_id, resource_name, resource_type,
                   total_reservations, total_attended, total_cancelled, total_no_show,
                   reserved_minutes, blocked_minutes, updated_at
            FROM report_resource_occupancy_by_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ?
            """;

        var cassandraDate = new LocalDate(date.Year, date.Month, date.Day);
        var stmt = new SimpleStatement(cql, tenantId, branchId, cassandraDate);
        var rowSet = await session.ExecuteAsync(stmt);
        var dateStr = date.ToString("yyyy-MM-dd");

        return rowSet.Select(row => new ResourceOccupancyItem(
            row.GetValue<Guid>("resource_id"),
            row.IsNull("resource_name") ? string.Empty : row.GetValue<string>("resource_name"),
            row.IsNull("resource_type") ? string.Empty : row.GetValue<string>("resource_type"),
            dateStr,
            row.GetValue<int>("total_reservations"),
            row.GetValue<int>("total_attended"),
            row.GetValue<int>("total_cancelled"),
            row.GetValue<int>("total_no_show"),
            row.GetValue<int>("reserved_minutes"),
            row.GetValue<int>("blocked_minutes"),
            row.IsNull("updated_at") ? null : row.GetValue<DateTimeOffset>("updated_at")
        )).ToList();
    }

    // ── HU-026 ───────────────────────────────────────────────────────────────

    [HttpGet("services/top")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<IActionResult> GetTopServices(
        [FromQuery] string? month = null,
        [FromQuery] string? monthFrom = null,
        [FromQuery] string? monthTo = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var role = User.FindFirstValue("roles") ?? string.Empty;
        if (role == "client") return Forbid();

        Guid resolvedTenantId;
        if (role == "super_admin")
        {
            if (!tenantId.HasValue)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "super_admin debe especificar tenantId."));
            }
            resolvedTenantId = tenantId.Value;
        }
        else
        {
            if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out resolvedTenantId))
                return Forbid();
        }

        // Resolve month range
        DateOnly from;
        DateOnly to;

        if (month is not null)
        {
            if (!DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "month debe tener formato yyyy-MM."));
            }
            to = from;
        }
        else if (monthFrom is not null || monthTo is not null)
        {
            from = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            to = from;

            if (monthFrom is not null &&
                !DateOnly.TryParseExact(monthFrom + "-01", "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "monthFrom debe tener formato yyyy-MM."));
            }

            if (monthTo is not null &&
                !DateOnly.TryParseExact(monthTo + "-01", "yyyy-MM-dd", out to))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "monthTo debe tener formato yyyy-MM."));
            }

            if (to < from)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "monthTo debe ser mayor o igual a monthFrom."));
            }

            // Max 24 months
            var totalMonths = (to.Year - from.Year) * 12 + (to.Month - from.Month);
            if (totalMonths > 23)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "El rango no puede exceder 24 meses."));
            }
        }
        else
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "Se requiere month, monthFrom o monthTo."));
        }

        // Query each month and aggregate by serviceId in memory
        var aggregated = new Dictionary<Guid, (string Name, int Created, int Cancelled,
            int Attended, int NoShow, int Minutes)>();

        var current = new DateOnly(from.Year, from.Month, 1);
        var end = new DateOnly(to.Year, to.Month, 1);
        while (current <= end)
        {
            var yearMonth = current.ToString("yyyy-MM");
            await AggregateServiceMonthAsync(resolvedTenantId, yearMonth, aggregated);
            current = current.AddMonths(1);
        }

        var ranked = aggregated
            .OrderByDescending(kv => kv.Value.Created)
            .Select((kv, idx) => new ServiceSummaryItem(
                Rank: idx + 1,
                ServiceId: kv.Key,
                ServiceName: kv.Value.Name,
                TotalCreated: kv.Value.Created,
                TotalCancelled: kv.Value.Cancelled,
                TotalAttended: kv.Value.Attended,
                TotalNoShow: kv.Value.NoShow,
                TotalReservedMinutes: kv.Value.Minutes))
            .ToList();

        var response = new ServiceRankingResponse(
            PeriodFrom: from.ToString("yyyy-MM"),
            PeriodTo: to.ToString("yyyy-MM"),
            Services: ranked);

        return Ok(ApiResponse<ServiceRankingResponse>.Ok(response));
    }

    private async Task AggregateServiceMonthAsync(
        Guid tenantId,
        string yearMonth,
        Dictionary<Guid, (string Name, int Created, int Cancelled,
            int Attended, int NoShow, int Minutes)> aggregated)
    {
        const string cql = """
            SELECT service_id, service_name, total_created, total_cancelled,
                   total_attended, total_no_show, total_reserved_minutes
            FROM report_service_summary_by_month
            WHERE tenant_id = ? AND year_month = ?
            """;

        var stmt = new SimpleStatement(cql, tenantId, yearMonth);
        var rowSet = await session.ExecuteAsync(stmt);

        foreach (var row in rowSet)
        {
            var id = row.GetValue<Guid>("service_id");
            var name = row.IsNull("service_name") ? string.Empty : row.GetValue<string>("service_name");
            var created = row.GetValue<int>("total_created");
            var cancelled = row.GetValue<int>("total_cancelled");
            var attended = row.GetValue<int>("total_attended");
            var noShow = row.GetValue<int>("total_no_show");
            var minutes = row.GetValue<int>("total_reserved_minutes");

            if (aggregated.TryGetValue(id, out var existing))
            {
                aggregated[id] = (name,
                    existing.Created + created,
                    existing.Cancelled + cancelled,
                    existing.Attended + attended,
                    existing.NoShow + noShow,
                    existing.Minutes + minutes);
            }
            else
            {
                aggregated[id] = (name, created, cancelled, attended, noShow, minutes);
            }
        }
    }

    // ── HU-027 ───────────────────────────────────────────────────────────────

    [HttpGet("peak-hours")]
    [Authorize(Policy = "AuthenticatedUser")]
    public async Task<IActionResult> GetPeakHours(
        [FromQuery] Guid? branchId = null,
        [FromQuery] string? date = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var role = User.FindFirstValue("roles") ?? string.Empty;
        if (role == "client") return Forbid();

        if (!branchId.HasValue)
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "branchId es requerido."));
        }

        Guid resolvedTenantId;
        if (role == "super_admin")
        {
            if (!tenantId.HasValue)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "super_admin debe especificar tenantId."));
            }
            resolvedTenantId = tenantId.Value;
        }
        else
        {
            if (!Guid.TryParse(User.FindFirstValue("tenant_id"), out resolvedTenantId))
                return Forbid();
        }

        if (role == "branch_admin")
        {
            if (!Guid.TryParse(User.FindFirstValue("branch_id"), out var claimedBranch)
                || branchId.Value != claimedBranch)
                return Forbid();
        }

        // Resolve date range (same logic as resource occupancy)
        DateOnly from;
        DateOnly to;

        if (date is not null)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "date debe tener formato yyyy-MM-dd."));
            }
            to = from;
        }
        else if (dateFrom is not null || dateTo is not null)
        {
            from = DateOnly.FromDateTime(DateTime.UtcNow);
            to = from;

            if (dateFrom is not null && !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", out from))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateFrom debe tener formato yyyy-MM-dd."));
            }

            if (dateTo is not null && !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", out to))
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateTo debe tener formato yyyy-MM-dd."));
            }

            if (to < from)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "dateTo debe ser mayor o igual a dateFrom."));
            }

            if (to.DayNumber - from.DayNumber > 30)
            {
                return BadRequest(ApiResponse<object>.Failure(
                    "VALIDATION_ERROR", "El rango de fechas no puede exceder 31 días."));
            }
        }
        else
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR", "Se requiere date, dateFrom o dateTo."));
        }

        // Query each day and aggregate by hour_of_day
        var aggregated = new Dictionary<int, (int Created, int Attended, int Cancelled)>();

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            await AggregatePeakHoursForDayAsync(
                resolvedTenantId, branchId.Value, d, aggregated);
        }

        var hours = aggregated
            .OrderBy(kv => kv.Key)
            .Select(kv => new PeakHourItem(
                HourOfDay: kv.Key,
                TotalCreated: kv.Value.Created,
                TotalAttended: kv.Value.Attended,
                TotalCancelled: kv.Value.Cancelled))
            .ToList();

        var response = new PeakHoursResponse(
            BranchId: branchId.Value,
            PeriodFrom: from.ToString("yyyy-MM-dd"),
            PeriodTo: to.ToString("yyyy-MM-dd"),
            Hours: hours);

        return Ok(ApiResponse<PeakHoursResponse>.Ok(response));
    }

    private async Task AggregatePeakHoursForDayAsync(
        Guid tenantId,
        Guid branchId,
        DateOnly date,
        Dictionary<int, (int Created, int Attended, int Cancelled)> aggregated)
    {
        const string cql = """
            SELECT hour_of_day, total_created, total_attended, total_cancelled
            FROM report_peak_hours_by_branch_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ?
            """;

        var cassandraDate = new LocalDate(date.Year, date.Month, date.Day);
        var stmt = new SimpleStatement(cql, tenantId, branchId, cassandraDate);
        var rowSet = await session.ExecuteAsync(stmt);

        foreach (var row in rowSet)
        {
            var hour = row.GetValue<int>("hour_of_day");
            var created = row.GetValue<int>("total_created");
            var attended = row.GetValue<int>("total_attended");
            var cancelled = row.GetValue<int>("total_cancelled");

            if (aggregated.TryGetValue(hour, out var existing))
            {
                aggregated[hour] = (
                    existing.Created + created,
                    existing.Attended + attended,
                    existing.Cancelled + cancelled);
            }
            else
            {
                aggregated[hour] = (created, attended, cancelled);
            }
        }
    }

    // ── HU-024 helpers ────────────────────────────────────────────────────────

    private async Task<DailySummaryResponse> GetTenantSummaryAsync(Guid tenantId, DateOnly date)
    {
        const string cql = """
            SELECT total_created, total_confirmed, total_cancelled, total_attended,
                   total_no_show, total_reserved_minutes, updated_at
            FROM report_daily_summary_by_tenant
            WHERE tenant_id = ? AND report_date = ?
            """;

        var cassandraDate = new LocalDate(date.Year, date.Month, date.Day);
        var stmt = new SimpleStatement(cql, tenantId, cassandraDate);
        var rowSet = await session.ExecuteAsync(stmt);
        var row = rowSet.FirstOrDefault();

        if (row is null)
        {
            return new DailySummaryResponse(
                tenantId, null, null,
                date.ToString("yyyy-MM-dd"),
                0, 0, 0, 0, 0, 0,
                null, "PENDING_SYNC");
        }

        return new DailySummaryResponse(
            tenantId, null, null,
            date.ToString("yyyy-MM-dd"),
            row.GetValue<int>("total_created"),
            row.GetValue<int>("total_confirmed"),
            row.GetValue<int>("total_cancelled"),
            row.GetValue<int>("total_attended"),
            row.GetValue<int>("total_no_show"),
            row.GetValue<int>("total_reserved_minutes"),
            row.IsNull("updated_at") ? null : row.GetValue<DateTimeOffset>("updated_at"),
            "OK");
    }

    private async Task<DailySummaryResponse> GetBranchSummaryAsync(
        Guid tenantId, Guid branchId, DateOnly date)
    {
        const string cql = """
            SELECT branch_name, total_created, total_confirmed, total_cancelled, total_attended,
                   total_no_show, total_reserved_minutes, updated_at
            FROM report_daily_summary_by_branch
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ?
            """;

        var cassandraDate = new LocalDate(date.Year, date.Month, date.Day);
        var stmt = new SimpleStatement(cql, tenantId, branchId, cassandraDate);
        var rowSet = await session.ExecuteAsync(stmt);
        var row = rowSet.FirstOrDefault();

        if (row is null)
        {
            return new DailySummaryResponse(
                tenantId, branchId, null,
                date.ToString("yyyy-MM-dd"),
                0, 0, 0, 0, 0, 0,
                null, "PENDING_SYNC");
        }

        return new DailySummaryResponse(
            tenantId, branchId,
            row.IsNull("branch_name") ? null : row.GetValue<string>("branch_name"),
            date.ToString("yyyy-MM-dd"),
            row.GetValue<int>("total_created"),
            row.GetValue<int>("total_confirmed"),
            row.GetValue<int>("total_cancelled"),
            row.GetValue<int>("total_attended"),
            row.GetValue<int>("total_no_show"),
            row.GetValue<int>("total_reserved_minutes"),
            row.IsNull("updated_at") ? null : row.GetValue<DateTimeOffset>("updated_at"),
            "OK");
    }
}
