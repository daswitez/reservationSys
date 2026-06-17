using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using BookingService.Common;
using BookingService.Data;
using BookingService.Domain;
using BookingService.Features.Reservations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ReservationsController(BookingDbContext dbContext) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Crea una reserva confirmada para el cliente autenticado.</summary>
    [Authorize(Policy = "ClientOnly")]
    [HttpPost("reservations")]
    [ProducesResponseType(typeof(ApiResponse<ReservationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var clientUserId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        if (request.BranchId == Guid.Empty)
        {
            return ValidationError("branchId es requerido.");
        }

        if (request.ServiceId == Guid.Empty)
        {
            return ValidationError("serviceId es requerido.");
        }

        if (request.ResourceId == Guid.Empty)
        {
            return ValidationError("resourceId debe ser un UUID valido cuando se envia.");
        }

        if (request.StartAt == default)
        {
            return ValidationError("startAt es requerido.");
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        try
        {
            var branch = await dbContext.Branches
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.BranchId == request.BranchId && entity.Status == "active",
                    cancellationToken);

            if (branch is null)
            {
                return NotFound(ApiResponse<object>.Failure(
                    "BRANCH_NOT_FOUND",
                    $"No existe una sucursal activa '{request.BranchId}'."));
            }

            var tenant = await dbContext.Tenants
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.TenantId == branch.TenantId && entity.Status == "active",
                    cancellationToken);

            if (tenant is null)
            {
                return NotFound(ApiResponse<object>.Failure(
                    "TENANT_NOT_FOUND",
                    "El tenant de la sucursal no existe o no esta activo."));
            }

            var service = await dbContext.Services
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    entity => entity.ServiceId == request.ServiceId
                        && entity.TenantId == branch.TenantId
                        && entity.Status == "active",
                    cancellationToken);

            if (service is null)
            {
                return NotFound(ApiResponse<object>.Failure(
                    "SERVICE_NOT_FOUND",
                    $"No existe un servicio activo '{request.ServiceId}' para la sucursal indicada."));
            }

            var serviceAvailableInBranch = await dbContext.BranchServices
                .AsNoTracking()
                .AnyAsync(
                    entity => entity.TenantId == branch.TenantId
                        && entity.BranchId == branch.BranchId
                        && entity.ServiceId == service.ServiceId
                        && entity.Status == "active",
                    cancellationToken);

            if (!serviceAvailableInBranch)
            {
                return NotFound(ApiResponse<object>.Failure(
                    "SERVICE_NOT_FOUND",
                    $"El servicio '{request.ServiceId}' no esta activo en la sucursal '{request.BranchId}'."));
            }

            var branchTimeZone = FindTimeZone(branch.Timezone);
            var startAtInBranch = TimeZoneInfo.ConvertTime(request.StartAt, branchTimeZone);
            var endAtInBranch = startAtInBranch.AddMinutes(service.DurationMinutes);
            if (startAtInBranch <= TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, branchTimeZone))
            {
                return ValidationError("No se puede reservar un horario pasado.");
            }

            if (startAtInBranch.Date != endAtInBranch.Date)
            {
                return ValidationError("La reserva debe iniciar y terminar el mismo dia local de la sucursal.");
            }

            var candidates = await GetCompatibleResourcesAsync(
                branch.TenantId,
                branch.BranchId,
                service.ServiceId,
                request.ResourceId,
                cancellationToken);

            if (request.ResourceId.HasValue && candidates.Count == 0)
            {
                return NotFound(ApiResponse<object>.Failure(
                    "RESOURCE_NOT_FOUND",
                    $"El recurso '{request.ResourceId}' no esta activo o no es compatible con el servicio."));
            }

            if (candidates.Count == 0)
            {
                return Conflict(ApiResponse<object>.Failure(
                    "RESOURCE_NOT_AVAILABLE",
                    "No hay recursos activos compatibles para el servicio seleccionado."));
            }

            var selected = await SelectAvailableResourceAsync(
                candidates,
                branch,
                service,
                startAtInBranch,
                endAtInBranch,
                cancellationToken);

            if (selected.Resource is null)
            {
                return selected.Reason switch
                {
                    UnavailableReason.Blocked => Conflict(ApiResponse<object>.Failure(
                        "RESOURCE_BLOCKED",
                        "El recurso esta bloqueado para el horario seleccionado.")),
                    UnavailableReason.NoSchedule => Conflict(ApiResponse<object>.Failure(
                        "RESOURCE_NOT_AVAILABLE",
                        "El recurso no trabaja en el horario seleccionado.")),
                    _ => Conflict(ApiResponse<object>.Failure(
                        "SLOT_ALREADY_TAKEN",
                        "El horario seleccionado ya no esta disponible."))
                };
            }

            var now = DateTimeOffset.UtcNow;
            var reservation = new Reservation
            {
                ReservationId = Guid.NewGuid(),
                TenantId = branch.TenantId,
                BranchId = branch.BranchId,
                ClientUserId = clientUserId,
                ServiceId = service.ServiceId,
                ResourceId = selected.Resource.ResourceId,
                CreatedByUserId = clientUserId,
                StartAt = request.StartAt.ToUniversalTime(),
                EndAt = request.StartAt.AddMinutes(service.DurationMinutes).ToUniversalTime(),
                Status = "CONFIRMED",
                ChannelOrigin = "web",
                Notes = NormalizeOptional(request.Notes),
                CreatedAt = now,
                UpdatedAt = now
            };

            var history = new ReservationHistory
            {
                HistoryId = Guid.NewGuid(),
                TenantId = reservation.TenantId,
                ReservationId = reservation.ReservationId,
                UserId = clientUserId,
                PreviousStatus = null,
                NewStatus = "CONFIRMED",
                Action = "CREATED",
                CreatedAt = now
            };

            var eventId = Guid.NewGuid();
            var outbox = new ReservationEventOutbox
            {
                EventId = eventId,
                TenantId = reservation.TenantId,
                EventType = "ReservationCreated",
                AggregateId = reservation.ReservationId,
                Payload = BuildReservationCreatedPayload(
                    eventId,
                    reservation,
                    branch,
                    service,
                    selected.Resource,
                    service.DurationMinutes,
                    now),
                Status = "PENDING",
                Attempts = 0,
                CreatedAt = now
            };

            dbContext.Reservations.Add(reservation);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.ReservationHistory.Add(history);
            dbContext.ReservationEventOutbox.Add(outbox);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { reservationId = reservation.ReservationId },
                ApiResponse<ReservationResponse>.Ok(ToResponse(reservation, startAtInBranch, endAtInBranch)));
        }
        catch (DbUpdateException exception) when (IsSlotConflict(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(ApiResponse<object>.Failure(
                "SLOT_ALREADY_TAKEN",
                "El horario seleccionado ya no esta disponible."));
        }
    }

    /// <summary>Obtiene una reserva por identificador.</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpGet("reservations/{reservationId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid reservationId, CancellationToken cancellationToken)
    {
        var reservation = await dbContext.Reservations
            .AsNoTracking()
            .SingleOrDefaultAsync(entity => entity.ReservationId == reservationId, cancellationToken);

        if (reservation is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "RESERVATION_NOT_FOUND",
                $"No existe la reserva '{reservationId}'."));
        }

        if (!CanAccessReservation(reservation, allowClient: true, out var failure))
        {
            return failure!;
        }

        return Ok(ApiResponse<ReservationResponse>.Ok(ToResponse(reservation)));
    }

    /// <summary>Cancela una reserva confirmada. El cliente solo puede cancelar la propia.</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPatch("reservations/{reservationId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        Guid reservationId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CancelReservationRequest? request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        var reservation = await dbContext.Reservations
            .SingleOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);

        if (reservation is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "RESERVATION_NOT_FOUND",
                $"No existe la reserva '{reservationId}'."));
        }

        if (!CanAccessReservation(reservation, allowClient: true, out var failure))
        {
            return failure!;
        }

        if (reservation.Status != "CONFIRMED")
        {
            return Conflict(ApiResponse<object>.Failure(
                "RESERVATION_NOT_CANCELLABLE",
                "Solo se pueden cancelar reservas con estado CONFIRMED."));
        }

        var now = DateTimeOffset.UtcNow;
        reservation.Status = "CANCELLED";
        reservation.UpdatedAt = now;

        dbContext.ReservationHistory.Add(new ReservationHistory
        {
            HistoryId = Guid.NewGuid(),
            TenantId = reservation.TenantId,
            ReservationId = reservation.ReservationId,
            UserId = userId,
            PreviousStatus = "CONFIRMED",
            NewStatus = "CANCELLED",
            Action = "CANCELLED",
            Reason = NormalizeOptional(request?.Reason),
            CreatedAt = now
        });

        var eventId = Guid.NewGuid();
        dbContext.ReservationEventOutbox.Add(new ReservationEventOutbox
        {
            EventId = eventId,
            TenantId = reservation.TenantId,
            EventType = "ReservationCancelled",
            AggregateId = reservation.ReservationId,
            Payload = BuildReservationCancelledPayload(eventId, reservation, now),
            Status = "PENDING",
            Attempts = 0,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ReservationResponse>.Ok(ToResponse(reservation)));
    }

    /// <summary>Marca una reserva como atendida. Solo usuarios internos (tenant_admin, branch_admin, super_admin).</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPatch("reservations/{reservationId:guid}/attend")]
    [ProducesResponseType(typeof(ApiResponse<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Attend(Guid reservationId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        var reservation = await dbContext.Reservations
            .SingleOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);

        if (reservation is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "RESERVATION_NOT_FOUND",
                $"No existe la reserva '{reservationId}'."));
        }

        if (!CanAccessReservation(reservation, allowClient: false, out var failure))
        {
            return failure!;
        }

        if (reservation.Status != "CONFIRMED")
        {
            return Conflict(ApiResponse<object>.Failure(
                "RESERVATION_NOT_ATTENDABLE",
                "Solo se pueden marcar como atendidas reservas con estado CONFIRMED."));
        }

        var now = DateTimeOffset.UtcNow;
        reservation.Status = "ATTENDED";
        reservation.UpdatedAt = now;

        dbContext.ReservationHistory.Add(new ReservationHistory
        {
            HistoryId = Guid.NewGuid(),
            TenantId = reservation.TenantId,
            ReservationId = reservation.ReservationId,
            UserId = userId,
            PreviousStatus = "CONFIRMED",
            NewStatus = "ATTENDED",
            Action = "ATTENDED",
            CreatedAt = now
        });

        var eventId = Guid.NewGuid();
        dbContext.ReservationEventOutbox.Add(new ReservationEventOutbox
        {
            EventId = eventId,
            TenantId = reservation.TenantId,
            EventType = "ReservationAttended",
            AggregateId = reservation.ReservationId,
            Payload = BuildReservationAttendedPayload(eventId, reservation, now),
            Status = "PENDING",
            Attempts = 0,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ReservationResponse>.Ok(ToResponse(reservation)));
    }

    /// <summary>Marca que el cliente no asistió. Solo usuarios internos (tenant_admin, branch_admin, super_admin).</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPatch("reservations/{reservationId:guid}/no-show")]
    [ProducesResponseType(typeof(ApiResponse<ReservationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> NoShow(Guid reservationId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        var reservation = await dbContext.Reservations
            .SingleOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);

        if (reservation is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "RESERVATION_NOT_FOUND",
                $"No existe la reserva '{reservationId}'."));
        }

        if (!CanAccessReservation(reservation, allowClient: false, out var failure))
        {
            return failure!;
        }

        if (reservation.Status != "CONFIRMED")
        {
            return Conflict(ApiResponse<object>.Failure(
                "RESERVATION_NOT_NO_SHOWABLE",
                "Solo se pueden marcar como no-show reservas con estado CONFIRMED."));
        }

        var now = DateTimeOffset.UtcNow;
        reservation.Status = "NO_SHOW";
        reservation.UpdatedAt = now;

        dbContext.ReservationHistory.Add(new ReservationHistory
        {
            HistoryId = Guid.NewGuid(),
            TenantId = reservation.TenantId,
            ReservationId = reservation.ReservationId,
            UserId = userId,
            PreviousStatus = "CONFIRMED",
            NewStatus = "NO_SHOW",
            Action = "NO_SHOW",
            CreatedAt = now
        });

        var eventId = Guid.NewGuid();
        dbContext.ReservationEventOutbox.Add(new ReservationEventOutbox
        {
            EventId = eventId,
            TenantId = reservation.TenantId,
            EventType = "ReservationNoShow",
            AggregateId = reservation.ReservationId,
            Payload = BuildReservationNoShowPayload(eventId, reservation, now),
            Status = "PENDING",
            Attempts = 0,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ReservationResponse>.Ok(ToResponse(reservation)));
    }

    /// <summary>
    /// Busca reservas con filtros opcionales. Solo usuarios internos.
    /// tenant_admin ve su tenant; branch_admin ve su sucursal (claim branch_id).
    /// Máximo 200 resultados, ordenados por StartAt descendente.
    /// </summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpGet("admin/reservations")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ReservationSearchItem>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Search(
        [FromQuery] Guid? clientUserId = null,
        [FromQuery] Guid? branchId = null,
        [FromQuery] Guid? serviceId = null,
        [FromQuery] Guid? resourceId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null,
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

        DateOnly? parsedDateFrom = null;
        DateOnly? parsedDateTo = null;

        if (!string.IsNullOrWhiteSpace(dateFrom) &&
            !DateOnly.TryParseExact(dateFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var df))
        {
            return ValidationError("dateFrom debe tener formato YYYY-MM-DD.");
        }
        else if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            parsedDateFrom = DateOnly.ParseExact(dateFrom, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(dateTo) &&
            !DateOnly.TryParseExact(dateTo, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt))
        {
            return ValidationError("dateTo debe tener formato YYYY-MM-DD.");
        }
        else if (!string.IsNullOrWhiteSpace(dateTo))
        {
            parsedDateTo = DateOnly.ParseExact(dateTo, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        var query = dbContext.Reservations.AsNoTracking();

        if (!User.IsInRole("super_admin"))
        {
            var tenantIdClaim = User.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                return Unauthorized(ApiResponse<object>.Failure(
                    "UNAUTHORIZED",
                    "El JWT no contiene tenant_id valido."));
            }
            query = query.Where(r => r.TenantId == tenantId);
        }

        if (User.IsInRole("branch_admin"))
        {
            var claimBranchId = User.FindFirstValue("branch_id");
            if (!Guid.TryParse(claimBranchId, out var assignedBranchId))
            {
                return Unauthorized(ApiResponse<object>.Failure(
                    "UNAUTHORIZED",
                    "El JWT no contiene branch_id valido."));
            }
            query = query.Where(r => r.BranchId == assignedBranchId);
        }

        if (clientUserId.HasValue)
            query = query.Where(r => r.ClientUserId == clientUserId.Value);

        if (branchId.HasValue)
            query = query.Where(r => r.BranchId == branchId.Value);

        if (serviceId.HasValue)
            query = query.Where(r => r.ServiceId == serviceId.Value);

        if (resourceId.HasValue)
            query = query.Where(r => r.ResourceId == resourceId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status.Trim().ToUpperInvariant());

        if (parsedDateFrom.HasValue)
        {
            var fromUtc = parsedDateFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.StartAt >= fromUtc);
        }

        if (parsedDateTo.HasValue)
        {
            var toUtc = parsedDateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            query = query.Where(r => r.StartAt < toUtc);
        }

        var reservations = await query
            .OrderByDescending(r => r.StartAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var reservationIds = reservations.Select(r => r.ReservationId).ToList();
        var histories = await dbContext.ReservationHistory
            .AsNoTracking()
            .Where(h => reservationIds.Contains(h.ReservationId))
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken);

        var historiesById = histories
            .GroupBy(h => h.ReservationId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var items = reservations.Select(r =>
        {
            var historyEntries = historiesById.TryGetValue(r.ReservationId, out var entries)
                ? entries.Select(e => new ReservationHistoryItem(
                    e.Action,
                    e.PreviousStatus,
                    e.NewStatus,
                    e.Reason,
                    e.UserId,
                    e.CreatedAt)).ToList()
                : (IReadOnlyList<ReservationHistoryItem>)[];

            return new ReservationSearchItem(
                r.ReservationId,
                r.TenantId,
                r.BranchId,
                r.ServiceId,
                r.ResourceId,
                r.ClientUserId,
                r.Status,
                r.StartAt,
                r.EndAt,
                r.Notes,
                r.CreatedAt,
                historyEntries);
        }).ToList();

        return Ok(ApiResponse<IReadOnlyList<ReservationSearchItem>>.Ok(items));
    }

    private async Task<List<CompatibleResource>> GetCompatibleResourcesAsync(
        Guid tenantId,
        Guid branchId,
        Guid serviceId,
        Guid? resourceId,
        CancellationToken cancellationToken)
    {
        var query =
            from serviceResource in dbContext.ServiceResources.AsNoTracking()
            join resource in dbContext.Resources.AsNoTracking()
                on serviceResource.ResourceId equals resource.ResourceId
            where serviceResource.TenantId == tenantId
                && serviceResource.ServiceId == serviceId
                && serviceResource.Status == "active"
                && resource.TenantId == tenantId
                && resource.BranchId == branchId
                && resource.Status == "active"
            select new
            {
                resource.ResourceId,
                resource.Name,
                serviceResource.Priority
            };

        if (resourceId.HasValue)
        {
            query = query.Where(resource => resource.ResourceId == resourceId.Value);
        }

        return await query
            .OrderBy(resource => resource.Priority)
            .ThenBy(resource => resource.Name)
            .Select(resource => new CompatibleResource(resource.ResourceId, resource.Name, resource.Priority))
            .ToListAsync(cancellationToken);
    }

    private async Task<ResourceSelection> SelectAvailableResourceAsync(
        IReadOnlyList<CompatibleResource> candidates,
        Branch branch,
        BookingService.Domain.Service service,
        DateTimeOffset startAtInBranch,
        DateTimeOffset endAtInBranch,
        CancellationToken cancellationToken)
    {
        var requestedDate = DateOnly.FromDateTime(startAtInBranch.DateTime);
        var startTime = TimeOnly.FromDateTime(startAtInBranch.DateTime);
        var endTime = TimeOnly.FromDateTime(endAtInBranch.DateTime);
        var dayOfWeek = ToIsoDayOfWeek(requestedDate.DayOfWeek);
        var startAtUtc = startAtInBranch.ToUniversalTime();
        var endAtUtc = endAtInBranch.ToUniversalTime();
        var anyScheduleMatched = false;
        var anyBlocked = false;

        foreach (var resource in candidates)
        {
            var hasSchedule = await dbContext.ResourceSchedules
                .AsNoTracking()
                .AnyAsync(schedule => schedule.TenantId == branch.TenantId
                    && schedule.BranchId == branch.BranchId
                    && schedule.ResourceId == resource.ResourceId
                    && schedule.DayOfWeek == dayOfWeek
                    && schedule.Status == "active"
                    && (schedule.ValidFrom == null || schedule.ValidFrom <= requestedDate)
                    && (schedule.ValidTo == null || schedule.ValidTo >= requestedDate)
                    && schedule.StartTime <= startTime
                    && schedule.EndTime >= endTime,
                    cancellationToken);

            if (!hasSchedule)
            {
                continue;
            }

            anyScheduleMatched = true;

            var hasBlock = await dbContext.ResourceBlocks
                .AsNoTracking()
                .AnyAsync(block => block.TenantId == branch.TenantId
                    && block.BranchId == branch.BranchId
                    && block.ResourceId == resource.ResourceId
                    && block.Status == "ACTIVE"
                    && block.StartAt < endAtUtc
                    && block.EndAt > startAtUtc,
                    cancellationToken);

            if (hasBlock)
            {
                anyBlocked = true;
                continue;
            }

            var hasReservation = await dbContext.Reservations
                .AsNoTracking()
                .AnyAsync(reservation => reservation.TenantId == branch.TenantId
                    && reservation.BranchId == branch.BranchId
                    && reservation.ResourceId == resource.ResourceId
                    && reservation.Status == "CONFIRMED"
                    && reservation.StartAt < endAtUtc
                    && reservation.EndAt > startAtUtc,
                    cancellationToken);

            if (!hasReservation)
            {
                return new ResourceSelection(resource, UnavailableReason.None);
            }
        }

        if (!anyScheduleMatched)
        {
            return new ResourceSelection(null, UnavailableReason.NoSchedule);
        }

        return new ResourceSelection(null, anyBlocked ? UnavailableReason.Blocked : UnavailableReason.Taken);
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue("user_id");
        return Guid.TryParse(value, out userId);
    }

    private bool CanAccessReservation(
        Reservation reservation,
        bool allowClient,
        out IActionResult? failure)
    {
        failure = null;

        if (User.IsInRole("super_admin"))
        {
            return true;
        }

        if (User.IsInRole("client"))
        {
            if (!allowClient)
            {
                failure = Forbid();
                return false;
            }

            if (!TryGetUserId(out var userId))
            {
                failure = Unauthorized(ApiResponse<object>.Failure(
                    "UNAUTHORIZED",
                    "El JWT no contiene user_id valido."));
                return false;
            }

            if (reservation.ClientUserId == userId)
            {
                return true;
            }

            failure = Forbid();
            return false;
        }

        if (User.IsInRole("branch_admin"))
        {
            var tenantIdClaim = User.FindFirstValue("tenant_id");
            var branchIdClaim = User.FindFirstValue("branch_id");
            if (!Guid.TryParse(tenantIdClaim, out var tenantId) ||
                !Guid.TryParse(branchIdClaim, out var branchId))
            {
                failure = Unauthorized(ApiResponse<object>.Failure(
                    "UNAUTHORIZED",
                    "El JWT no contiene tenant_id o branch_id valido."));
                return false;
            }

            if (reservation.TenantId == tenantId && reservation.BranchId == branchId)
            {
                return true;
            }

            failure = Forbid();
            return false;
        }

        if (User.IsInRole("tenant_admin"))
        {
            var tenantIdClaim = User.FindFirstValue("tenant_id");
            if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            {
                failure = Unauthorized(ApiResponse<object>.Failure(
                    "UNAUTHORIZED",
                    "El JWT no contiene tenant_id valido."));
                return false;
            }

            if (reservation.TenantId == tenantId)
            {
                return true;
            }

            failure = Forbid();
            return false;
        }

        failure = Forbid();
        return false;
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

    private static short ToIsoDayOfWeek(DayOfWeek dayOfWeek) =>
        dayOfWeek == DayOfWeek.Sunday ? (short)7 : (short)dayOfWeek;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSlotConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException
        && postgresException.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.UniqueViolation;

    private static string BuildReservationCancelledPayload(
        Guid eventId,
        Reservation reservation,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ReservationCancelled",
            occurredAt = occurredAt.ToUniversalTime(),
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ReservationId,
            reservation.StartAt,
            reservation.EndAt,
            reservation.Status
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildReservationAttendedPayload(
        Guid eventId,
        Reservation reservation,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ReservationAttended",
            occurredAt = occurredAt.ToUniversalTime(),
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ReservationId,
            reservation.StartAt,
            reservation.EndAt,
            reservation.Status
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildReservationNoShowPayload(
        Guid eventId,
        Reservation reservation,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ReservationNoShow",
            occurredAt = occurredAt.ToUniversalTime(),
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ReservationId,
            reservation.StartAt,
            reservation.EndAt,
            reservation.Status
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildReservationCreatedPayload(
        Guid eventId,
        Reservation reservation,
        Branch branch,
        Domain.Service service,
        CompatibleResource resource,
        int durationMinutes,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ReservationCreated",
            occurredAt = occurredAt.ToUniversalTime(),
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ReservationId,
            reservation.StartAt,
            reservation.EndAt,
            reservation.Status,
            durationMinutes,
            serviceName = service.Name,
            branchName = branch.Name,
            resourceName = resource.Name
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static ReservationResponse ToResponse(Reservation reservation) =>
        new(
            reservation.ReservationId,
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ClientUserId,
            reservation.Status,
            reservation.StartAt,
            reservation.EndAt,
            reservation.Notes,
            reservation.CreatedAt);

    private static ReservationResponse ToResponse(
        Reservation reservation,
        DateTimeOffset startAt,
        DateTimeOffset endAt) =>
        new(
            reservation.ReservationId,
            reservation.TenantId,
            reservation.BranchId,
            reservation.ServiceId,
            reservation.ResourceId,
            reservation.ClientUserId,
            reservation.Status,
            startAt,
            endAt,
            reservation.Notes,
            reservation.CreatedAt);

    private sealed record CompatibleResource(Guid ResourceId, string Name, int Priority);

    private sealed record ResourceSelection(CompatibleResource? Resource, UnavailableReason Reason);

    private enum UnavailableReason
    {
        None,
        NoSchedule,
        Blocked,
        Taken
    }
}
