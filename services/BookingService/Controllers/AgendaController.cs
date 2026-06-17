using BookingService.Common;
using BookingService.Data;
using BookingService.Features.Agenda;
using BookingService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class AgendaController(
    BookingDbContext dbContext,
    BookingAuthorizationService authorization) : ControllerBase
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
        if (!authorization.TryGetUserId(User, out _))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        if (!authorization.EnsureInternalUser(User, out var internalFailure))
        {
            return internalFailure!;
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

        if (!authorization.CanAccessBranch(User, branch.TenantId, branch.BranchId, out var failure))
        {
            return failure!;
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

        var reservationEntities = await reservationsQuery
            .OrderBy(r => r.StartAt)
            .ToListAsync(cancellationToken);

        var reservations = reservationEntities
            .Select(r => new AgendaReservationItem(
                r.ReservationId,
                r.ResourceId,
                r.ServiceId,
                r.ClientUserId,
                r.Status,
                TimeZoneInfo.ConvertTime(r.StartAt, tz),
                TimeZoneInfo.ConvertTime(r.EndAt, tz),
                r.Notes))
            .ToList();

        var blocksQuery = dbContext.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.BranchId == branchId
                && b.StartAt < endUtc
                && b.EndAt > startUtc
                && b.Status == "ACTIVE");

        if (resourceId.HasValue)
            blocksQuery = blocksQuery.Where(b => b.ResourceId == resourceId.Value);

        var blockEntities = await blocksQuery
            .OrderBy(b => b.StartAt)
            .ToListAsync(cancellationToken);

        var blocks = blockEntities
            .Select(b => new AgendaBlockItem(
                b.BlockId,
                b.ResourceId,
                b.Reason,
                b.BlockType,
                b.Status,
                TimeZoneInfo.ConvertTime(b.StartAt, tz),
                TimeZoneInfo.ConvertTime(b.EndAt, tz)))
            .ToList();

        return Ok(ApiResponse<AgendaResponse>.Ok(new AgendaResponse(
            parsedDate,
            branchId,
            reservations,
            blocks)));
    }

    private static BadRequestObjectResult ValidationError(string message) =>
        new(ApiResponse<object>.Failure("VALIDATION_ERROR", message));

    private static TimeZoneInfo FindTimeZone(string timezone)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return TimeZoneInfo.Utc; }
    }
}
