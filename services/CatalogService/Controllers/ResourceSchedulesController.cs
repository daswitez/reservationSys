using System.Security.Claims;
using CatalogService.Common;
using CatalogService.Data;
using CatalogService.Domain;
using CatalogService.Features.ResourceSchedules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ResourceSchedulesController(CatalogDbContext dbContext) : ControllerBase
{
    /// <summary>Lista horarios base de recursos del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("resource-schedules")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ResourceScheduleResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? branchId,
        [FromQuery] Guid? resourceId,
        [FromQuery] short? dayOfWeek,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (dayOfWeek is < 1 or > 7)
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "El dia de semana debe estar entre 1 y 7."));
        }

        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "El estado debe ser 'active' o 'inactive'."));
        }

        var tenantId = GetTenantId();
        var query = dbContext.ResourceSchedules
            .AsNoTracking()
            .Where(schedule => schedule.TenantId == tenantId);

        if (branchId.HasValue)
        {
            query = query.Where(schedule => schedule.BranchId == branchId.Value);
        }

        if (resourceId.HasValue)
        {
            query = query.Where(schedule => schedule.ResourceId == resourceId.Value);
        }

        if (dayOfWeek.HasValue)
        {
            query = query.Where(schedule => schedule.DayOfWeek == dayOfWeek.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(schedule => schedule.Status == status);
        }

        var schedules = await query
            .OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime)
            .Select(schedule => ToResponse(schedule))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ResourceScheduleResponse>>.Ok(schedules));
    }

    /// <summary>Obtiene un horario base del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("resource-schedules/{scheduleId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await FindOwnedScheduleAsync(scheduleId, true, cancellationToken);

        return schedule is null
            ? ScheduleNotFound(scheduleId)
            : Ok(ApiResponse<ResourceScheduleResponse>.Ok(ToResponse(schedule)));
    }

    /// <summary>Crea horario base para un recurso del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("resource-schedules")]
    [ProducesResponseType(typeof(ApiResponse<ResourceScheduleResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateResourceScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var validationResult = await ValidateBranchAndResourceAsync(
            request.BranchId,
            request.ResourceId,
            tenantId,
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var schedule = new ResourceSchedule
        {
            ScheduleId = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = request.BranchId,
            ResourceId = request.ResourceId,
            DayOfWeek = request.DayOfWeek,
            StartTime = ParseTime(request.StartTime),
            EndTime = ParseTime(request.EndTime),
            ValidFrom = ParseOptionalDate(request.ValidFrom),
            ValidTo = ParseOptionalDate(request.ValidTo),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ResourceSchedules.Add(schedule);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { scheduleId = schedule.ScheduleId },
            ApiResponse<ResourceScheduleResponse>.Ok(ToResponse(schedule)));
    }

    /// <summary>Reemplaza los datos editables de un horario base.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPut("resource-schedules/{scheduleId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid scheduleId,
        [FromBody] UpdateResourceScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var schedule = await FindOwnedScheduleAsync(scheduleId, false, cancellationToken);

        if (schedule is null)
        {
            return ScheduleNotFound(scheduleId);
        }

        var validationResult = await ValidateBranchAndResourceAsync(
            request.BranchId,
            request.ResourceId,
            tenantId,
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        schedule.BranchId = request.BranchId;
        schedule.ResourceId = request.ResourceId;
        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartTime = ParseTime(request.StartTime);
        schedule.EndTime = ParseTime(request.EndTime);
        schedule.ValidFrom = ParseOptionalDate(request.ValidFrom);
        schedule.ValidTo = ParseOptionalDate(request.ValidTo);
        schedule.Status = request.Status;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceScheduleResponse>.Ok(ToResponse(schedule)));
    }

    /// <summary>Activa o desactiva un horario base.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPatch("resource-schedules/{scheduleId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<ResourceScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid scheduleId,
        [FromBody] UpdateResourceScheduleStatusRequest request,
        CancellationToken cancellationToken)
    {
        var schedule = await FindOwnedScheduleAsync(scheduleId, false, cancellationToken);

        if (schedule is null)
        {
            return ScheduleNotFound(scheduleId);
        }

        schedule.Status = request.Status;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceScheduleResponse>.Ok(ToResponse(schedule)));
    }

    /// <summary>Realiza la baja logica de un horario base.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("resource-schedules/{scheduleId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceScheduleResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid scheduleId, CancellationToken cancellationToken)
    {
        var schedule = await FindOwnedScheduleAsync(scheduleId, false, cancellationToken);

        if (schedule is null)
        {
            return ScheduleNotFound(scheduleId);
        }

        schedule.Status = "inactive";
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceScheduleResponse>.Ok(ToResponse(schedule)));
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);

    private Task<ResourceSchedule?> FindOwnedScheduleAsync(
        Guid scheduleId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = dbContext.ResourceSchedules
            .Where(schedule => schedule.ScheduleId == scheduleId && schedule.TenantId == tenantId);

        return (asNoTracking ? query.AsNoTracking() : query)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task<IActionResult?> ValidateBranchAndResourceAsync(
        Guid branchId,
        Guid resourceId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Branches.AsNoTracking().AnyAsync(
                branch => branch.BranchId == branchId && branch.TenantId == tenantId,
                cancellationToken))
        {
            return BranchNotFound(branchId);
        }

        var resourceBranchId = await dbContext.Resources
            .AsNoTracking()
            .Where(resource => resource.ResourceId == resourceId && resource.TenantId == tenantId)
            .Select(resource => (Guid?)resource.BranchId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!resourceBranchId.HasValue)
        {
            return ResourceNotFound(resourceId);
        }

        if (resourceBranchId.Value != branchId)
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "El recurso indicado no pertenece a la sucursal enviada."));
        }

        return null;
    }

    private NotFoundObjectResult ScheduleNotFound(Guid scheduleId) =>
        NotFound(ApiResponse<object>.Failure(
            "RESOURCE_SCHEDULE_NOT_FOUND",
            $"No existe el horario base '{scheduleId}' en el tenant autenticado."));

    private NotFoundObjectResult BranchNotFound(Guid branchId) =>
        NotFound(ApiResponse<object>.Failure(
            "BRANCH_NOT_FOUND",
            $"No existe la sucursal '{branchId}' en el tenant autenticado."));

    private NotFoundObjectResult ResourceNotFound(Guid resourceId) =>
        NotFound(ApiResponse<object>.Failure(
            "RESOURCE_NOT_FOUND",
            $"No existe el recurso '{resourceId}' en el tenant autenticado."));

    private static TimeOnly ParseTime(string value)
    {
        _ = ResourceScheduleRequestValidation.TryParseTime(value, out var time);
        return time;
    }

    private static DateOnly? ParseOptionalDate(string? value)
    {
        _ = ResourceScheduleRequestValidation.TryParseOptionalDate(value, out var date);
        return date;
    }

    private static ResourceScheduleResponse ToResponse(ResourceSchedule schedule) =>
        new(
            schedule.ScheduleId,
            schedule.TenantId,
            schedule.BranchId,
            schedule.ResourceId,
            schedule.DayOfWeek,
            schedule.StartTime.ToString("HH:mm"),
            schedule.EndTime.ToString("HH:mm"),
            schedule.ValidFrom?.ToString("yyyy-MM-dd"),
            schedule.ValidTo?.ToString("yyyy-MM-dd"),
            schedule.Status,
            schedule.CreatedAt);
}
