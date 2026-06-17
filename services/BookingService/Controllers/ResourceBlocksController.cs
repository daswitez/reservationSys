using System.Text.Json;
using BookingService.Common;
using BookingService.Data;
using BookingService.Domain;
using BookingService.Features.ResourceBlocks;
using BookingService.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ResourceBlocksController(
    BookingDbContext dbContext,
    BookingAuthorizationService authorization) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Crea un bloqueo de recurso. Solo usuarios internos.</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPost("resource-blocks")]
    [ProducesResponseType(typeof(ApiResponse<ResourceBlockResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateResourceBlockRequest request,
        CancellationToken cancellationToken)
    {
        if (!authorization.TryGetUserId(User, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        if (!authorization.EnsureInternalUser(User, out var internalFailure))
        {
            return internalFailure!;
        }

        if (request.ResourceId == Guid.Empty)
            return ValidationError("resourceId es requerido.");

        if (request.StartAt == default)
            return ValidationError("startAt es requerido.");

        if (request.EndAt == default)
            return ValidationError("endAt es requerido.");

        if (request.EndAt <= request.StartAt)
            return ValidationError("endAt debe ser posterior a startAt.");

        var resource = await dbContext.Resources
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.ResourceId == request.ResourceId && r.Status == "active",
                cancellationToken);

        if (resource is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "RESOURCE_NOT_FOUND",
                $"No existe un recurso activo '{request.ResourceId}'."));
        }

        if (!authorization.CanAccessResource(User, resource, out var failure))
        {
            return failure!;
        }

        var startAt = request.StartAt.ToUniversalTime();
        var endAt = request.EndAt.ToUniversalTime();

        var hasOverlap = await dbContext.ResourceBlocks
            .AsNoTracking()
            .AnyAsync(
                block => block.TenantId == resource.TenantId
                    && block.ResourceId == resource.ResourceId
                    && block.Status == "ACTIVE"
                    && block.StartAt < endAt
                    && block.EndAt > startAt,
                cancellationToken);

        if (hasOverlap)
        {
            return Conflict(ApiResponse<object>.Failure(
                "BLOCK_OVERLAP",
                "Ya existe un bloqueo activo que se solapa con el rango indicado."));
        }

        var now = DateTimeOffset.UtcNow;
        var block = new ResourceBlock
        {
            BlockId = Guid.NewGuid(),
            TenantId = resource.TenantId,
            BranchId = resource.BranchId,
            ResourceId = resource.ResourceId,
            StartAt = startAt,
            EndAt = endAt,
            Reason = NormalizeOptional(request.Reason),
            BlockType = NormalizeOptional(request.BlockType) ?? "manual",
            Status = "ACTIVE",
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        };

        var eventId = Guid.NewGuid();
        dbContext.ResourceBlocks.Add(block);
        dbContext.ReservationEventOutbox.Add(new ReservationEventOutbox
        {
            EventId = eventId,
            TenantId = block.TenantId,
            EventType = "ResourceBlockCreated",
            AggregateId = block.BlockId,
            Payload = BuildResourceBlockCreatedPayload(eventId, block, now),
            Status = "PENDING",
            Attempts = 0,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { blockId = block.BlockId },
            ApiResponse<ResourceBlockResponse>.Ok(ToResponse(block)));
    }

    /// <summary>Cancela un bloqueo activo. Solo usuarios internos.</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPatch("resource-blocks/{blockId:guid}/cancel")]
    [ProducesResponseType(typeof(ApiResponse<ResourceBlockResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid blockId, CancellationToken cancellationToken)
    {
        if (!authorization.TryGetUserId(User, out var userId))
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "UNAUTHORIZED",
                "El JWT no contiene user_id valido."));
        }

        if (!authorization.EnsureInternalUser(User, out var internalFailure))
        {
            return internalFailure!;
        }

        var block = await dbContext.ResourceBlocks
            .SingleOrDefaultAsync(b => b.BlockId == blockId, cancellationToken);

        if (block is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "BLOCK_NOT_FOUND",
                $"No existe el bloqueo '{blockId}'."));
        }

        if (!authorization.CanAccessBlock(User, block, out var failure))
        {
            return failure!;
        }

        if (block.Status != "ACTIVE")
        {
            return Conflict(ApiResponse<object>.Failure(
                "BLOCK_NOT_CANCELLABLE",
                "Solo se pueden cancelar bloqueos con estado ACTIVE."));
        }

        var now = DateTimeOffset.UtcNow;
        block.Status = "CANCELLED";
        block.UpdatedAt = now;

        var eventId = Guid.NewGuid();
        dbContext.ReservationEventOutbox.Add(new ReservationEventOutbox
        {
            EventId = eventId,
            TenantId = block.TenantId,
            EventType = "ResourceBlockCancelled",
            AggregateId = block.BlockId,
            Payload = BuildResourceBlockCancelledPayload(eventId, block, userId, now),
            Status = "PENDING",
            Attempts = 0,
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceBlockResponse>.Ok(ToResponse(block)));
    }

    /// <summary>Obtiene un bloqueo de recurso por identificador.</summary>
    [Authorize(Policy = "AuthenticatedUser")]
    [HttpGet("resource-blocks/{blockId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceBlockResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid blockId, CancellationToken cancellationToken)
    {
        if (!authorization.EnsureInternalUser(User, out var internalFailure))
        {
            return internalFailure!;
        }

        var block = await dbContext.ResourceBlocks
            .AsNoTracking()
            .SingleOrDefaultAsync(b => b.BlockId == blockId, cancellationToken);

        if (block is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "BLOCK_NOT_FOUND",
                $"No existe el bloqueo '{blockId}'."));
        }

        if (!authorization.CanAccessBlock(User, block, out var failure))
        {
            return failure!;
        }

        return Ok(ApiResponse<ResourceBlockResponse>.Ok(ToResponse(block)));
    }

    private static BadRequestObjectResult ValidationError(string message) =>
        new(ApiResponse<object>.Failure("VALIDATION_ERROR", message));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildResourceBlockCancelledPayload(
        Guid eventId,
        ResourceBlock block,
        Guid cancelledByUserId,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ResourceBlockCancelled",
            occurredAt = occurredAt.ToUniversalTime(),
            block.TenantId,
            block.BranchId,
            block.ResourceId,
            block.BlockId,
            block.StartAt,
            block.EndAt,
            block.Status,
            cancelledByUserId
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string BuildResourceBlockCreatedPayload(
        Guid eventId,
        ResourceBlock block,
        DateTimeOffset occurredAt)
    {
        var payload = new
        {
            eventId,
            eventType = "ResourceBlockCreated",
            occurredAt = occurredAt.ToUniversalTime(),
            block.TenantId,
            block.BranchId,
            block.ResourceId,
            block.BlockId,
            block.StartAt,
            block.EndAt,
            block.Reason,
            block.BlockType,
            block.Status
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static ResourceBlockResponse ToResponse(ResourceBlock block) =>
        new(
            block.BlockId,
            block.TenantId,
            block.BranchId,
            block.ResourceId,
            block.StartAt,
            block.EndAt,
            block.Reason,
            block.BlockType,
            block.Status,
            block.CreatedByUserId,
            block.CreatedAt);
}
