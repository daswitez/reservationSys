using System.Security.Claims;
using BookingService.Common;
using BookingService.Data;
using BookingService.Features.Agenda;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class AgendaController(BookingDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Retorna la agenda de una sucursal para una fecha: reservas activas y bloqueos.
    /// branch_admin solo puede consultar su propia sucursal (claim branch_id).
    /// </summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpGet("admin/agenda")]
    [ProducesResponseType(typeof(ApiResponse<AgendaResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAgenda(
        [FromQuery] Guid branchId,
        [FromQuery] string date,
        [FromQuery] Guid? resourceId = null,
        [FromQuery] Guid? serviceId = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out _))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        if (User.IsInRole("client"))
        {
            return Forbid();
        }

        if (branchId == Guid.Empty)
            return ValidationError("branchId es requerido.");

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsedDate))
        {
            return ValidationError("date debe tener formato YYYY-MM-DD.");
        }

        var branch = await dbContext.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.BranchId == branchId && b.Status == "active", cancellationToken);

        if (branch is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "BRANCH_NOT_FOUND",
                $"No existe una sucursal activa '{branchId}'."));
        }

        if (!User.IsInRole("super_admin"))
        {
            var tenantIdClaim = User.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdClaim, out var tenantId) || branch.TenantId != tenantId)
                return Forbid();
        }

        if (User.IsInRole("branch_admin"))
        {
            var branchIdClaim = User.FindFirstValue("branch_id");
            if (!Guid.TryParse(branchIdClaim, out var claimBranchId) || claimBranchId != branchId)
                return Forbid();
        }

        var tz = FindTimeZone(branch.Timezone);
        var localStart = parsedDate.ToDateTime(TimeOnly.MinValue);
        var localEnd = parsedDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, tz);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, tz);

        var reservationsQuery = dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.BranchId == branchId
                && r.StartAt < endUtc
                && r.EndAt > startUtc);

        if (resourceId.HasValue)
            reservationsQuery = reservationsQuery.Where(r => r.ResourceId == resourceId.Value);

        if (serviceId.HasValue)
            reservationsQuery = reservationsQuery.Where(r => r.ServiceId == serviceId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            reservationsQuery = reservationsQuery.Where(r => r.Status == status.Trim().ToUpperInvariant());

        var reservations = await reservationsQuery
            .OrderBy(r => r.StartAt)
            .Select(r => new AgendaReservationItem(
                r.ReservationId,
                r.ResourceId,
                r.ServiceId,
                r.ClientUserId,
                r.Status,
                r.StartAt,
                r.EndAt,
                r.Notes))
            .ToListAsync(cancellationToken);

        var blocksQuery = dbContext.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.BranchId == branchId
                && b.StartAt < endUtc
                && b.EndAt > startUtc
                && b.Status == "ACTIVE");

        if (resourceId.HasValue)
            blocksQuery = blocksQuery.Where(b => b.ResourceId == resourceId.Value);

        var blocks = await blocksQuery
            .OrderBy(b => b.StartAt)
            .Select(b => new AgendaBlockItem(
                b.BlockId,
                b.ResourceId,
                b.Reason,
                b.BlockType,
                b.Status,
                b.StartAt,
                b.EndAt))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<AgendaResponse>.Ok(new AgendaResponse(
            parsedDate,
            branchId,
            reservations,
            blocks)));
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private static BadRequestObjectResult ValidationError(string message) =>
        new(ApiResponse<object>.Failure("VALIDATION_ERROR", message));

    private static TimeZoneInfo FindTimeZone(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
}
