using System.Globalization;
using BookingService.Common;
using BookingService.Data;
using BookingService.Features.Availability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class AvailabilityController(BookingDbContext dbContext) : ControllerBase
{
    private const int SlotMinutes = 15;

    /// <summary>Consulta slots disponibles para un servicio, sucursal y fecha.</summary>
    [AllowAnonymous]
    [HttpGet("availability")]
    [ProducesResponseType(typeof(ApiResponse<AvailabilityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] string? tenantSlug,
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? serviceId,
        [FromQuery] string? date,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantSlug))
        {
            return ValidationError("tenantSlug es requerido.");
        }

        if (!branchId.HasValue || branchId.Value == Guid.Empty)
        {
            return ValidationError("branchId es requerido y debe ser un UUID valido.");
        }

        if (!serviceId.HasValue || serviceId.Value == Guid.Empty)
        {
            return ValidationError("serviceId es requerido y debe ser un UUID valido.");
        }

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var requestedDate))
        {
            return ValidationError("date es requerido con formato yyyy-MM-dd.");
        }

        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.Slug == normalizedSlug && entity.Status == "active",
                cancellationToken);

        if (tenant is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "TENANT_NOT_FOUND",
                $"No existe un tenant activo con slug '{normalizedSlug}'."));
        }

        var branch = await dbContext.Branches
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.BranchId == branchId.Value
                    && entity.TenantId == tenant.TenantId
                    && entity.Status == "active",
                cancellationToken);

        if (branch is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "BRANCH_NOT_FOUND",
                $"No existe una sucursal activa '{branchId}' para el tenant indicado."));
        }

        var service = await dbContext.Services
            .AsNoTracking()
            .SingleOrDefaultAsync(
                entity => entity.ServiceId == serviceId.Value
                    && entity.TenantId == tenant.TenantId
                    && entity.Status == "active",
                cancellationToken);

        if (service is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "SERVICE_NOT_FOUND",
                $"No existe un servicio activo '{serviceId}' para el tenant indicado."));
        }

        var serviceAvailableInBranch = await dbContext.BranchServices
            .AsNoTracking()
            .AnyAsync(
                entity => entity.TenantId == tenant.TenantId
                    && entity.BranchId == branch.BranchId
                    && entity.ServiceId == service.ServiceId
                    && entity.Status == "active",
                cancellationToken);

        if (!serviceAvailableInBranch)
        {
            return NotFound(ApiResponse<object>.Failure(
                "SERVICE_NOT_FOUND",
                $"El servicio '{serviceId}' no esta activo en la sucursal '{branchId}'."));
        }

        var branchTimeZone = FindTimeZone(branch.Timezone);
        var nowInBranch = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, branchTimeZone);
        var requestedDayOfWeek = ToIsoDayOfWeek(requestedDate.DayOfWeek);

        var compatibleResources = await (
                from serviceResource in dbContext.ServiceResources.AsNoTracking()
                join resource in dbContext.Resources.AsNoTracking()
                    on serviceResource.ResourceId equals resource.ResourceId
                where serviceResource.TenantId == tenant.TenantId
                    && serviceResource.ServiceId == service.ServiceId
                    && serviceResource.Status == "active"
                    && resource.TenantId == tenant.TenantId
                    && resource.BranchId == branch.BranchId
                    && resource.Status == "active"
                orderby serviceResource.Priority, resource.Name
                select new CompatibleResource(resource.ResourceId, resource.Name, serviceResource.Priority))
            .ToListAsync(cancellationToken);

        if (compatibleResources.Count == 0)
        {
            return Ok(ApiResponse<AvailabilityResponse>.Ok(new AvailabilityResponse(
                branch.BranchId,
                service.ServiceId,
                requestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                SlotMinutes,
                [])));
        }

        var resourceIds = compatibleResources.Select(resource => resource.ResourceId).ToArray();
        var schedules = await dbContext.ResourceSchedules
            .AsNoTracking()
            .Where(schedule => schedule.TenantId == tenant.TenantId
                && schedule.BranchId == branch.BranchId
                && resourceIds.Contains(schedule.ResourceId)
                && schedule.DayOfWeek == requestedDayOfWeek
                && schedule.Status == "active"
                && (schedule.ValidFrom == null || schedule.ValidFrom <= requestedDate)
                && (schedule.ValidTo == null || schedule.ValidTo >= requestedDate))
            .ToListAsync(cancellationToken);

        var dayStart = ToBranchInstant(requestedDate, TimeOnly.MinValue, branchTimeZone).ToUniversalTime();
        var dayEnd = ToBranchInstant(requestedDate.AddDays(1), TimeOnly.MinValue, branchTimeZone).ToUniversalTime();

        var reservations = await dbContext.Reservations
            .AsNoTracking()
            .Where(reservation => reservation.TenantId == tenant.TenantId
                && reservation.BranchId == branch.BranchId
                && resourceIds.Contains(reservation.ResourceId)
                && reservation.Status == "CONFIRMED"
                && reservation.StartAt < dayEnd
                && reservation.EndAt > dayStart)
            .Select(reservation => new BusyInterval(reservation.ResourceId, reservation.StartAt, reservation.EndAt))
            .ToListAsync(cancellationToken);

        var blocks = await dbContext.ResourceBlocks
            .AsNoTracking()
            .Where(block => block.TenantId == tenant.TenantId
                && block.BranchId == branch.BranchId
                && resourceIds.Contains(block.ResourceId)
                && block.Status == "ACTIVE"
                && block.StartAt < dayEnd
                && block.EndAt > dayStart)
            .Select(block => new BusyInterval(block.ResourceId, block.StartAt, block.EndAt))
            .ToListAsync(cancellationToken);

        var busyIntervals = reservations.Concat(blocks)
            .GroupBy(interval => interval.ResourceId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var resourcesById = compatibleResources.ToDictionary(resource => resource.ResourceId);
        var availableSlots = new List<AvailableSlotResponse>();

        foreach (var schedule in schedules.OrderBy(schedule => schedule.StartTime))
        {
            var latestStart = schedule.EndTime.AddMinutes(-service.DurationMinutes);
            for (var slotStart = schedule.StartTime; slotStart <= latestStart; slotStart = slotStart.AddMinutes(SlotMinutes))
            {
                var slotEnd = slotStart.AddMinutes(service.DurationMinutes);
                var startAt = ToBranchInstant(requestedDate, slotStart, branchTimeZone);
                if (startAt <= nowInBranch)
                {
                    continue;
                }

                var endAt = ToBranchInstant(requestedDate, slotEnd, branchTimeZone);
                var startAtUtc = startAt.ToUniversalTime();
                var endAtUtc = endAt.ToUniversalTime();

                if (busyIntervals.TryGetValue(schedule.ResourceId, out var intervals)
                    && intervals.Any(interval => Overlaps(startAtUtc, endAtUtc, interval.StartAt, interval.EndAt)))
                {
                    continue;
                }

                var resource = resourcesById[schedule.ResourceId];
                availableSlots.Add(new AvailableSlotResponse(
                    resource.ResourceId,
                    resource.Name,
                    startAt,
                    endAt));
            }
        }

        var response = new AvailabilityResponse(
            branch.BranchId,
            service.ServiceId,
            requestedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SlotMinutes,
            availableSlots
                .OrderBy(slot => slot.StartAt)
                .ThenBy(slot => slot.ResourceName)
                .ThenBy(slot => slot.ResourceId)
                .ToList());

        return Ok(ApiResponse<AvailabilityResponse>.Ok(response));
    }

    private static BadRequestObjectResult ValidationError(string message) =>
        new(ApiResponse<object>.Failure("VALIDATION_ERROR", message));

    private static TimeZoneInfo FindTimeZone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset ToBranchInstant(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var localDateTime = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, timeZone.GetUtcOffset(localDateTime));
    }

    private static short ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)dayOfWeek;

    private static bool Overlaps(
        DateTimeOffset startAt,
        DateTimeOffset endAt,
        DateTimeOffset busyStartAt,
        DateTimeOffset busyEndAt) =>
        startAt < busyEndAt.ToUniversalTime() && endAt > busyStartAt.ToUniversalTime();

    private sealed record CompatibleResource(Guid ResourceId, string Name, int Priority);

    private sealed record BusyInterval(Guid ResourceId, DateTimeOffset StartAt, DateTimeOffset EndAt);
}
