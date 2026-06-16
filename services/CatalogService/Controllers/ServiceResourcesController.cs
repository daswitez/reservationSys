using System.Security.Claims;
using CatalogService.Common;
using CatalogService.Data;
using CatalogService.Domain;
using CatalogService.Features.ServiceResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ServiceResourcesController(CatalogDbContext dbContext) : ControllerBase
{
    /// <summary>Lista los recursos asociados a un servicio del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("services/{serviceId:guid}/resources")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid serviceId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "El estado debe ser 'active' o 'inactive'."));
        }

        var tenantId = GetTenantId();
        if (!await ServiceBelongsToTenantAsync(serviceId, tenantId, cancellationToken))
        {
            return ServiceNotFound(serviceId);
        }

        var query = dbContext.ServiceResources
            .AsNoTracking()
            .Where(link => link.TenantId == tenantId && link.ServiceId == serviceId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(link => link.Status == status);
        }

        var links = await query
            .OrderBy(link => link.Priority)
            .ThenBy(link => link.ResourceId)
            .Select(link => ToResponse(link))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ServiceResourceResponse>>.Ok(links));
    }

    /// <summary>Lista recursos activos compatibles con un servicio activo.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("services/{serviceId:guid}/compatible-resources")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CompatibleResourceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListCompatibleResources(
        Guid serviceId,
        [FromQuery] Guid? branchId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (!await dbContext.Services.AsNoTracking().AnyAsync(
                service => service.ServiceId == serviceId &&
                    service.TenantId == tenantId &&
                    service.Status == "active",
                cancellationToken))
        {
            return ServiceNotFound(serviceId);
        }

        var query =
            from link in dbContext.ServiceResources.AsNoTracking()
            join resource in dbContext.Resources.AsNoTracking()
                on new { link.TenantId, link.ResourceId } equals new { resource.TenantId, resource.ResourceId }
            where link.TenantId == tenantId &&
                link.ServiceId == serviceId &&
                link.Status == "active" &&
                resource.Status == "active"
            select new { link, resource };

        if (branchId.HasValue)
        {
            query = query.Where(row => row.resource.BranchId == branchId.Value);
        }

        var resources = await query
            .OrderBy(row => row.link.Priority)
            .ThenBy(row => row.resource.Name)
            .Select(row => new CompatibleResourceResponse(
                row.resource.ResourceId,
                row.resource.TenantId,
                row.resource.BranchId,
                row.resource.Name,
                row.resource.ResourceType,
                row.resource.Description,
                row.resource.Capacity,
                row.link.Required,
                row.link.Priority))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<CompatibleResourceResponse>>.Ok(resources));
    }

    /// <summary>Asocia un recurso compatible con un servicio.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("services/{serviceId:guid}/resources/{resourceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResourceResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<ServiceResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Upsert(
        Guid serviceId,
        Guid resourceId,
        [FromBody] UpsertServiceResourceRequest? request,
        CancellationToken cancellationToken)
    {
        request ??= new UpsertServiceResourceRequest();
        var tenantId = GetTenantId();
        var validationResult = await ValidateServiceAndResourceAsync(
            serviceId,
            resourceId,
            tenantId,
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status;
        var existing = await dbContext.ServiceResources.SingleOrDefaultAsync(
            link => link.TenantId == tenantId &&
                link.ServiceId == serviceId &&
                link.ResourceId == resourceId,
            cancellationToken);

        if (existing is not null)
        {
            existing.Required = request.Required;
            existing.Priority = request.Priority;
            existing.Status = status;
            await dbContext.SaveChangesAsync(cancellationToken);

            return Ok(ApiResponse<ServiceResourceResponse>.Ok(ToResponse(existing)));
        }

        var link = new ServiceResource
        {
            TenantId = tenantId,
            ServiceId = serviceId,
            ResourceId = resourceId,
            Required = request.Required,
            Priority = request.Priority,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.ServiceResources.Add(link);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<ServiceResourceResponse>.Ok(ToResponse(link)));
    }

    /// <summary>Edita los atributos de compatibilidad entre servicio y recurso.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPut("services/{serviceId:guid}/resources/{resourceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid serviceId,
        Guid resourceId,
        [FromBody] UpsertServiceResourceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var validationResult = await ValidateServiceAndResourceAsync(
            serviceId,
            resourceId,
            tenantId,
            cancellationToken);

        if (validationResult is not null)
        {
            return validationResult;
        }

        var link = await FindOwnedLinkAsync(serviceId, resourceId, tenantId, cancellationToken);
        if (link is null)
        {
            return ServiceResourceNotFound(serviceId, resourceId);
        }

        link.Required = request.Required;
        link.Priority = request.Priority;
        link.Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ServiceResourceResponse>.Ok(ToResponse(link)));
    }

    /// <summary>Activa o desactiva la compatibilidad entre servicio y recurso.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPatch("services/{serviceId:guid}/resources/{resourceId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid serviceId,
        Guid resourceId,
        [FromBody] UpdateServiceResourceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var link = await FindOwnedLinkAsync(serviceId, resourceId, tenantId, cancellationToken);

        if (link is null)
        {
            return ServiceResourceNotFound(serviceId, resourceId);
        }

        link.Status = request.Status;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ServiceResourceResponse>.Ok(ToResponse(link)));
    }

    /// <summary>Deshabilita un recurso compatible para un servicio.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("services/{serviceId:guid}/resources/{resourceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid serviceId,
        Guid resourceId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var link = await FindOwnedLinkAsync(serviceId, resourceId, tenantId, cancellationToken);

        if (link is null)
        {
            return ServiceResourceNotFound(serviceId, resourceId);
        }

        link.Status = "inactive";
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);

    private async Task<IActionResult?> ValidateServiceAndResourceAsync(
        Guid serviceId,
        Guid resourceId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (!await ServiceBelongsToTenantAsync(serviceId, tenantId, cancellationToken))
        {
            return ServiceNotFound(serviceId);
        }

        if (!await ResourceBelongsToTenantAsync(resourceId, tenantId, cancellationToken))
        {
            return ResourceNotFound(resourceId);
        }

        return null;
    }

    private Task<bool> ServiceBelongsToTenantAsync(
        Guid serviceId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Services
            .AsNoTracking()
            .AnyAsync(service => service.ServiceId == serviceId && service.TenantId == tenantId, cancellationToken);

    private Task<bool> ResourceBelongsToTenantAsync(
        Guid resourceId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Resources
            .AsNoTracking()
            .AnyAsync(resource => resource.ResourceId == resourceId && resource.TenantId == tenantId, cancellationToken);

    private Task<ServiceResource?> FindOwnedLinkAsync(
        Guid serviceId,
        Guid resourceId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.ServiceResources.SingleOrDefaultAsync(
            link => link.TenantId == tenantId &&
                link.ServiceId == serviceId &&
                link.ResourceId == resourceId,
            cancellationToken);

    private NotFoundObjectResult ServiceNotFound(Guid serviceId) =>
        NotFound(ApiResponse<object>.Failure(
            "SERVICE_NOT_FOUND",
            $"No existe el servicio '{serviceId}' en el tenant autenticado."));

    private NotFoundObjectResult ResourceNotFound(Guid resourceId) =>
        NotFound(ApiResponse<object>.Failure(
            "RESOURCE_NOT_FOUND",
            $"No existe el recurso '{resourceId}' en el tenant autenticado."));

    private NotFoundObjectResult ServiceResourceNotFound(Guid serviceId, Guid resourceId) =>
        NotFound(ApiResponse<object>.Failure(
            "SERVICE_RESOURCE_NOT_FOUND",
            $"No existe una asociacion activa o historica entre el servicio '{serviceId}' y el recurso '{resourceId}' en el tenant autenticado."));

    private static ServiceResourceResponse ToResponse(ServiceResource link) =>
        new(
            link.TenantId,
            link.ServiceId,
            link.ResourceId,
            link.Required,
            link.Priority,
            link.Status,
            link.CreatedAt);
}
