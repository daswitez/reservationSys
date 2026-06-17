using System.Security.Claims;
using System.Text.Json;
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
    [HttpPost("/internal/report-events")]
    [AllowAnonymous]
    public async Task<IActionResult> IngestReportEvent(
        [FromBody] JsonElement payload,
        CancellationToken cancellationToken = default)
    {
        if (!TryReadGuid(payload, "eventId", out var eventId) ||
            !TryReadString(payload, "eventType", out var eventType) ||
            !TryReadGuid(payload, "tenantId", out var tenantId))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "eventId, eventType y tenantId son requeridos."));
        }

        if (!IsSupportedReportEvent(eventType))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "UNSUPPORTED_EVENT",
                $"El evento '{eventType}' no es soportado por Reporting."));
        }

        try
        {
            ValidateReportEventPayload(eventType, payload);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                exception.Message));
        }

        var claimed = await TryClaimEventAsync(eventId, tenantId, eventType);
        if (!claimed)
        {
            return Ok(ApiResponse<object>.Ok(new
            {
                status = "DUPLICATE",
                eventId
            }));
        }

        await ApplyReportEventAsync(eventType, payload);

        return Ok(ApiResponse<object>.Ok(new
        {
            status = "PROCESSED",
            eventId
        }));
    }

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

    private static bool IsSupportedReportEvent(string eventType) =>
        eventType is "ReservationCreated"
            or "ReservationCancelled"
            or "ReservationAttended"
            or "ReservationNoShow"
            or "ResourceBlockCreated"
            or "ResourceBlockCancelled";

    private async Task<bool> TryClaimEventAsync(Guid eventId, Guid tenantId, string eventType)
    {
        const string cql = """
            INSERT INTO report_processed_events (event_id, tenant_id, event_type, processed_at)
            VALUES (?, ?, ?, ?)
            IF NOT EXISTS
            """;

        var rowSet = await session.ExecuteAsync(new SimpleStatement(
            cql,
            eventId,
            tenantId,
            eventType,
            DateTimeOffset.UtcNow));
        var row = rowSet.FirstOrDefault();
        return row is not null && row.GetValue<bool>("[applied]");
    }

    private async Task ApplyReportEventAsync(string eventType, JsonElement payload)
    {
        switch (eventType)
        {
            case "ReservationCreated":
                await ApplyReservationCreatedAsync(payload);
                break;
            case "ReservationCancelled":
                await ApplyReservationCancelledAsync(payload);
                break;
            case "ReservationAttended":
                await ApplyReservationAttendedAsync(payload);
                break;
            case "ReservationNoShow":
                await ApplyReservationNoShowAsync(payload);
                break;
            case "ResourceBlockCreated":
                await ApplyResourceBlockDeltaAsync(payload, 1);
                break;
            case "ResourceBlockCancelled":
                await ApplyResourceBlockDeltaAsync(payload, -1);
                break;
        }
    }

    private static void ValidateReportEventPayload(string eventType, JsonElement payload)
    {
        _ = RequiredGuid(payload, "tenantId");
        _ = RequiredGuid(payload, "branchId");
        _ = RequiredGuid(payload, "resourceId");
        _ = RequiredDateTimeOffset(payload, "startAt");
        _ = GetDurationMinutes(payload);

        if (eventType.StartsWith("Reservation", StringComparison.Ordinal))
        {
            _ = RequiredGuid(payload, "serviceId");
            _ = RequiredGuid(payload, "reservationId");
        }
        else
        {
            _ = RequiredGuid(payload, "blockId");
        }
    }

    private async Task ApplyReservationCreatedAsync(JsonElement payload)
    {
        var tenantId = RequiredGuid(payload, "tenantId");
        var branchId = RequiredGuid(payload, "branchId");
        var serviceId = RequiredGuid(payload, "serviceId");
        var resourceId = RequiredGuid(payload, "resourceId");
        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var durationMinutes = GetDurationMinutes(payload);
        var reportDate = ToCassandraDate(startAt);
        var yearMonth = ToYearMonth(startAt);
        var updatedAt = DateTimeOffset.UtcNow;

        await UpsertTenantDailyAsync(
            tenantId, reportDate, 1, 1, 0, 0, 0, 0, durationMinutes, updatedAt);
        await UpsertBranchDailyAsync(
            tenantId, branchId, reportDate, OptionalString(payload, "branchName"),
            1, 1, 0, 0, 0, durationMinutes, updatedAt);
        await UpsertServiceMonthAsync(
            tenantId, yearMonth, serviceId, OptionalString(payload, "serviceName"),
            1, 0, 0, 0, durationMinutes, updatedAt);
        await UpsertResourceOccupancyAsync(
            tenantId, branchId, resourceId, reportDate,
            OptionalString(payload, "resourceName"), OptionalString(payload, "resourceType"),
            1, 0, 0, 0, durationMinutes, 0, updatedAt);
        await UpsertPeakHourAsync(
            tenantId, branchId, reportDate, startAt.UtcDateTime.Hour,
            OptionalString(payload, "branchName"), 1, 0, 0, updatedAt);
    }

    private async Task ApplyReservationCancelledAsync(JsonElement payload)
    {
        var tenantId = RequiredGuid(payload, "tenantId");
        var branchId = RequiredGuid(payload, "branchId");
        var serviceId = RequiredGuid(payload, "serviceId");
        var resourceId = RequiredGuid(payload, "resourceId");
        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var durationMinutes = GetDurationMinutes(payload);
        var reportDate = ToCassandraDate(startAt);
        var yearMonth = ToYearMonth(startAt);
        var updatedAt = DateTimeOffset.UtcNow;

        await UpsertTenantDailyAsync(
            tenantId, reportDate, 0, -1, 1, 0, 0, 0, -durationMinutes, updatedAt);
        await UpsertBranchDailyAsync(
            tenantId, branchId, reportDate, OptionalString(payload, "branchName"),
            0, -1, 1, 0, 0, -durationMinutes, updatedAt);
        await UpsertServiceMonthAsync(
            tenantId, yearMonth, serviceId, OptionalString(payload, "serviceName"),
            0, 1, 0, 0, -durationMinutes, updatedAt);
        await UpsertResourceOccupancyAsync(
            tenantId, branchId, resourceId, reportDate,
            OptionalString(payload, "resourceName"), OptionalString(payload, "resourceType"),
            0, 0, 1, 0, -durationMinutes, 0, updatedAt);
        await UpsertPeakHourAsync(
            tenantId, branchId, reportDate, startAt.UtcDateTime.Hour,
            OptionalString(payload, "branchName"), 0, 0, 1, updatedAt);
    }

    private async Task ApplyReservationAttendedAsync(JsonElement payload)
    {
        var tenantId = RequiredGuid(payload, "tenantId");
        var branchId = RequiredGuid(payload, "branchId");
        var serviceId = RequiredGuid(payload, "serviceId");
        var resourceId = RequiredGuid(payload, "resourceId");
        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var reportDate = ToCassandraDate(startAt);
        var yearMonth = ToYearMonth(startAt);
        var updatedAt = DateTimeOffset.UtcNow;

        await UpsertTenantDailyAsync(
            tenantId, reportDate, 0, -1, 0, 1, 0, 0, 0, updatedAt);
        await UpsertBranchDailyAsync(
            tenantId, branchId, reportDate, OptionalString(payload, "branchName"),
            0, -1, 0, 1, 0, 0, updatedAt);
        await UpsertServiceMonthAsync(
            tenantId, yearMonth, serviceId, OptionalString(payload, "serviceName"),
            0, 0, 1, 0, 0, updatedAt);
        await UpsertResourceOccupancyAsync(
            tenantId, branchId, resourceId, reportDate,
            OptionalString(payload, "resourceName"), OptionalString(payload, "resourceType"),
            0, 1, 0, 0, 0, 0, updatedAt);
        await UpsertPeakHourAsync(
            tenantId, branchId, reportDate, startAt.UtcDateTime.Hour,
            OptionalString(payload, "branchName"), 0, 1, 0, updatedAt);
    }

    private async Task ApplyReservationNoShowAsync(JsonElement payload)
    {
        var tenantId = RequiredGuid(payload, "tenantId");
        var branchId = RequiredGuid(payload, "branchId");
        var serviceId = RequiredGuid(payload, "serviceId");
        var resourceId = RequiredGuid(payload, "resourceId");
        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var reportDate = ToCassandraDate(startAt);
        var yearMonth = ToYearMonth(startAt);
        var updatedAt = DateTimeOffset.UtcNow;

        await UpsertTenantDailyAsync(
            tenantId, reportDate, 0, -1, 0, 0, 1, 0, 0, updatedAt);
        await UpsertBranchDailyAsync(
            tenantId, branchId, reportDate, OptionalString(payload, "branchName"),
            0, -1, 0, 0, 1, 0, updatedAt);
        await UpsertServiceMonthAsync(
            tenantId, yearMonth, serviceId, OptionalString(payload, "serviceName"),
            0, 0, 0, 1, 0, updatedAt);
        await UpsertResourceOccupancyAsync(
            tenantId, branchId, resourceId, reportDate,
            OptionalString(payload, "resourceName"), OptionalString(payload, "resourceType"),
            0, 0, 0, 1, 0, 0, updatedAt);
    }

    private async Task ApplyResourceBlockDeltaAsync(JsonElement payload, int direction)
    {
        var tenantId = RequiredGuid(payload, "tenantId");
        var branchId = RequiredGuid(payload, "branchId");
        var resourceId = RequiredGuid(payload, "resourceId");
        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var durationMinutes = GetDurationMinutes(payload) * direction;
        var reportDate = ToCassandraDate(startAt);
        var updatedAt = DateTimeOffset.UtcNow;

        await UpsertTenantDailyAsync(
            tenantId, reportDate, 0, 0, 0, 0, 0, durationMinutes, 0, updatedAt);
        await UpsertResourceOccupancyAsync(
            tenantId, branchId, resourceId, reportDate,
            OptionalString(payload, "resourceName"), OptionalString(payload, "resourceType"),
            0, 0, 0, 0, 0, durationMinutes, updatedAt);
    }

    private async Task UpsertTenantDailyAsync(
        Guid tenantId,
        LocalDate reportDate,
        int createdDelta,
        int confirmedDelta,
        int cancelledDelta,
        int attendedDelta,
        int noShowDelta,
        int blockedMinutesDelta,
        int reservedMinutesDelta,
        DateTimeOffset updatedAt)
    {
        const string selectCql = """
            SELECT total_created, total_confirmed, total_cancelled, total_attended,
                   total_no_show, total_blocked_minutes, total_reserved_minutes
            FROM report_daily_summary_by_tenant
            WHERE tenant_id = ? AND report_date = ?
            """;
        var current = (await session.ExecuteAsync(new SimpleStatement(selectCql, tenantId, reportDate)))
            .FirstOrDefault();

        const string insertCql = """
            INSERT INTO report_daily_summary_by_tenant
              (tenant_id, report_date, total_created, total_confirmed, total_cancelled,
               total_attended, total_no_show, total_blocked_minutes, total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            insertCql,
            tenantId,
            reportDate,
            Add(current, "total_created", createdDelta),
            Add(current, "total_confirmed", confirmedDelta),
            Add(current, "total_cancelled", cancelledDelta),
            Add(current, "total_attended", attendedDelta),
            Add(current, "total_no_show", noShowDelta),
            Add(current, "total_blocked_minutes", blockedMinutesDelta),
            Add(current, "total_reserved_minutes", reservedMinutesDelta),
            updatedAt));
    }

    private async Task UpsertBranchDailyAsync(
        Guid tenantId,
        Guid branchId,
        LocalDate reportDate,
        string? branchName,
        int createdDelta,
        int confirmedDelta,
        int cancelledDelta,
        int attendedDelta,
        int noShowDelta,
        int reservedMinutesDelta,
        DateTimeOffset updatedAt)
    {
        const string selectCql = """
            SELECT branch_name, total_created, total_confirmed, total_cancelled, total_attended,
                   total_no_show, total_reserved_minutes
            FROM report_daily_summary_by_branch
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ?
            """;
        var current = (await session.ExecuteAsync(new SimpleStatement(selectCql, tenantId, branchId, reportDate)))
            .FirstOrDefault();
        var resolvedBranchName = branchName ?? OptionalRowString(current, "branch_name") ?? string.Empty;

        const string insertCql = """
            INSERT INTO report_daily_summary_by_branch
              (tenant_id, branch_id, report_date, branch_name, total_created, total_confirmed,
               total_cancelled, total_attended, total_no_show, total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            insertCql,
            tenantId,
            branchId,
            reportDate,
            resolvedBranchName,
            Add(current, "total_created", createdDelta),
            Add(current, "total_confirmed", confirmedDelta),
            Add(current, "total_cancelled", cancelledDelta),
            Add(current, "total_attended", attendedDelta),
            Add(current, "total_no_show", noShowDelta),
            Add(current, "total_reserved_minutes", reservedMinutesDelta),
            updatedAt));
    }

    private async Task UpsertServiceMonthAsync(
        Guid tenantId,
        string yearMonth,
        Guid serviceId,
        string? serviceName,
        int createdDelta,
        int cancelledDelta,
        int attendedDelta,
        int noShowDelta,
        int reservedMinutesDelta,
        DateTimeOffset updatedAt)
    {
        const string selectCql = """
            SELECT service_name, total_created, total_cancelled, total_attended,
                   total_no_show, total_reserved_minutes
            FROM report_service_summary_by_month
            WHERE tenant_id = ? AND year_month = ? AND service_id = ?
            """;
        var current = (await session.ExecuteAsync(new SimpleStatement(selectCql, tenantId, yearMonth, serviceId)))
            .FirstOrDefault();
        var resolvedServiceName = serviceName ?? OptionalRowString(current, "service_name") ?? string.Empty;

        const string insertCql = """
            INSERT INTO report_service_summary_by_month
              (tenant_id, year_month, service_id, service_name, total_created, total_cancelled,
               total_attended, total_no_show, total_reserved_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            insertCql,
            tenantId,
            yearMonth,
            serviceId,
            resolvedServiceName,
            Add(current, "total_created", createdDelta),
            Add(current, "total_cancelled", cancelledDelta),
            Add(current, "total_attended", attendedDelta),
            Add(current, "total_no_show", noShowDelta),
            Add(current, "total_reserved_minutes", reservedMinutesDelta),
            updatedAt));
    }

    private async Task UpsertResourceOccupancyAsync(
        Guid tenantId,
        Guid branchId,
        Guid resourceId,
        LocalDate reportDate,
        string? resourceName,
        string? resourceType,
        int reservationsDelta,
        int attendedDelta,
        int cancelledDelta,
        int noShowDelta,
        int reservedMinutesDelta,
        int blockedMinutesDelta,
        DateTimeOffset updatedAt)
    {
        const string selectCql = """
            SELECT resource_name, resource_type, total_reservations, total_attended,
                   total_cancelled, total_no_show, reserved_minutes, blocked_minutes
            FROM report_resource_occupancy_by_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ? AND resource_id = ?
            """;
        var current = (await session.ExecuteAsync(new SimpleStatement(
                selectCql, tenantId, branchId, reportDate, resourceId)))
            .FirstOrDefault();
        var resolvedResourceName = resourceName ?? OptionalRowString(current, "resource_name") ?? string.Empty;
        var resolvedResourceType = resourceType ?? OptionalRowString(current, "resource_type") ?? string.Empty;

        const string insertCql = """
            INSERT INTO report_resource_occupancy_by_day
              (tenant_id, branch_id, resource_id, report_date,
               resource_name, resource_type,
               total_reservations, total_attended, total_cancelled, total_no_show,
               reserved_minutes, blocked_minutes, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            insertCql,
            tenantId,
            branchId,
            resourceId,
            reportDate,
            resolvedResourceName,
            resolvedResourceType,
            Add(current, "total_reservations", reservationsDelta),
            Add(current, "total_attended", attendedDelta),
            Add(current, "total_cancelled", cancelledDelta),
            Add(current, "total_no_show", noShowDelta),
            Add(current, "reserved_minutes", reservedMinutesDelta),
            Add(current, "blocked_minutes", blockedMinutesDelta),
            updatedAt));
    }

    private async Task UpsertPeakHourAsync(
        Guid tenantId,
        Guid branchId,
        LocalDate reportDate,
        int hourOfDay,
        string? branchName,
        int createdDelta,
        int attendedDelta,
        int cancelledDelta,
        DateTimeOffset updatedAt)
    {
        const string selectCql = """
            SELECT branch_name, total_created, total_attended, total_cancelled
            FROM report_peak_hours_by_branch_day
            WHERE tenant_id = ? AND branch_id = ? AND report_date = ? AND hour_of_day = ?
            """;
        var current = (await session.ExecuteAsync(new SimpleStatement(
                selectCql, tenantId, branchId, reportDate, hourOfDay)))
            .FirstOrDefault();
        var resolvedBranchName = branchName ?? OptionalRowString(current, "branch_name") ?? string.Empty;

        const string insertCql = """
            INSERT INTO report_peak_hours_by_branch_day
              (tenant_id, branch_id, report_date, hour_of_day, branch_name,
               total_created, total_attended, total_cancelled, updated_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """;
        await session.ExecuteAsync(new SimpleStatement(
            insertCql,
            tenantId,
            branchId,
            reportDate,
            hourOfDay,
            resolvedBranchName,
            Add(current, "total_created", createdDelta),
            Add(current, "total_attended", attendedDelta),
            Add(current, "total_cancelled", cancelledDelta),
            updatedAt));
    }

    private static int Add(Row? row, string column, int delta) =>
        Math.Max(0, (row is null || row.IsNull(column) ? 0 : row.GetValue<int>(column)) + delta);

    private static string? OptionalRowString(Row? row, string column) =>
        row is null || row.IsNull(column) ? null : row.GetValue<string>(column);

    private static bool TryReadGuid(JsonElement payload, string propertyName, out Guid value)
    {
        value = Guid.Empty;
        return payload.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryReadString(JsonElement payload, string propertyName, out string value)
    {
        value = string.Empty;
        if (!payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static Guid RequiredGuid(JsonElement payload, string propertyName) =>
        TryReadGuid(payload, propertyName, out var value)
            ? value
            : throw new InvalidOperationException($"{propertyName} es requerido.");

    private static DateTimeOffset RequiredDateTimeOffset(JsonElement payload, string propertyName)
    {
        if (!payload.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(property.GetString(), out var value))
        {
            throw new InvalidOperationException($"{propertyName} es requerido.");
        }

        return value;
    }

    private static string? OptionalString(JsonElement payload, string propertyName) =>
        payload.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int GetDurationMinutes(JsonElement payload)
    {
        if (payload.TryGetProperty("durationMinutes", out var durationProperty) &&
            durationProperty.ValueKind == JsonValueKind.Number &&
            durationProperty.TryGetInt32(out var durationMinutes))
        {
            return durationMinutes;
        }

        var startAt = RequiredDateTimeOffset(payload, "startAt");
        var endAt = RequiredDateTimeOffset(payload, "endAt");
        return Math.Max(0, (int)Math.Round((endAt - startAt).TotalMinutes));
    }

    private static LocalDate ToCassandraDate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return new LocalDate(utc.Year, utc.Month, utc.Day);
    }

    private static string ToYearMonth(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return $"{utc.Year:D4}-{utc.Month:D2}";
    }
}
