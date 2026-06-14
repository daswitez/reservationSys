using System.Security.Claims;
using CatalogService.Common;
using CatalogService.Data;
using CatalogService.Features.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CatalogDomainService = CatalogService.Domain.Service;

namespace CatalogService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ServicesController(CatalogDbContext dbContext) : ControllerBase
{
    /// <summary>Lista los servicios del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
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
        var query = dbContext.Services
            .AsNoTracking()
            .Where(service => service.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(service => service.Status == status);
        }

        var services = await query
            .OrderBy(service => service.Name)
            .Select(service => ToResponse(service))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ServiceResponse>>.Ok(services));
    }

    /// <summary>Obtiene un servicio del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("services/{serviceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid serviceId, CancellationToken cancellationToken)
    {
        var service = await FindOwnedServiceAsync(serviceId, true, cancellationToken);

        return service is null
            ? ServiceNotFound(serviceId)
            : Ok(ApiResponse<ServiceResponse>.Ok(ToResponse(service)));
    }

    /// <summary>Crea un servicio para el tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("services")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateServiceRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var service = new CatalogDomainService
        {
            ServiceId = Guid.NewGuid(),
            TenantId = GetTenantId(),
            Name = request.Name.Trim(),
            Description = request.Description.Trim(),
            DurationMinutes = request.DurationMinutes,
            ReferencePrice = request.ReferencePrice,
            Modality = request.Modality.Trim().ToLowerInvariant(),
            RequiresConfirmation = false,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Services.Add(service);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { serviceId = service.ServiceId },
            ApiResponse<ServiceResponse>.Ok(ToResponse(service)));
    }

    /// <summary>Reemplaza los datos editables de un servicio.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPut("services/{serviceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid serviceId,
        [FromBody] UpdateServiceRequest request,
        CancellationToken cancellationToken)
    {
        var service = await FindOwnedServiceAsync(serviceId, false, cancellationToken);

        if (service is null)
        {
            return ServiceNotFound(serviceId);
        }

        service.Name = request.Name.Trim();
        service.Description = request.Description.Trim();
        service.DurationMinutes = request.DurationMinutes;
        service.ReferencePrice = request.ReferencePrice;
        service.Modality = request.Modality.Trim().ToLowerInvariant();
        service.Status = request.Status;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ServiceResponse>.Ok(ToResponse(service)));
    }

    /// <summary>Activa o desactiva un servicio.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPatch("services/{serviceId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid serviceId,
        [FromBody] UpdateServiceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var service = await FindOwnedServiceAsync(serviceId, false, cancellationToken);

        if (service is null)
        {
            return ServiceNotFound(serviceId);
        }

        service.Status = request.Status;
        service.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ServiceResponse>.Ok(ToResponse(service)));
    }

    /// <summary>Realiza la baja logica de un servicio.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("services/{serviceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ServiceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid serviceId, CancellationToken cancellationToken)
    {
        var service = await FindOwnedServiceAsync(serviceId, false, cancellationToken);

        if (service is null)
        {
            return ServiceNotFound(serviceId);
        }

        service.Status = "inactive";
        service.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ServiceResponse>.Ok(ToResponse(service)));
    }

    /// <summary>Lista los servicios activos de un tenant activo para el portal publico.</summary>
    [AllowAnonymous]
    [HttpGet("public/tenants/{tenantSlug}/services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPublic(string tenantSlug, CancellationToken cancellationToken)
    {
        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenantId = await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Slug == normalizedSlug && tenant.Status == "active")
            .Select(tenant => (Guid?)tenant.TenantId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!tenantId.HasValue)
        {
            return NotFound(ApiResponse<object>.Failure(
                "TENANT_NOT_FOUND",
                $"No existe un tenant activo con el slug '{normalizedSlug}'."));
        }

        var services = await dbContext.Services
            .AsNoTracking()
            .Where(service => service.TenantId == tenantId.Value && service.Status == "active")
            .OrderBy(service => service.Name)
            .Select(service => ToResponse(service))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ServiceResponse>>.Ok(services));
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);

    private Task<CatalogDomainService?> FindOwnedServiceAsync(
        Guid serviceId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = dbContext.Services
            .Where(service => service.ServiceId == serviceId && service.TenantId == tenantId);

        return (asNoTracking ? query.AsNoTracking() : query)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private NotFoundObjectResult ServiceNotFound(Guid serviceId) =>
        NotFound(ApiResponse<object>.Failure(
            "SERVICE_NOT_FOUND",
            $"No existe el servicio '{serviceId}' en el tenant autenticado."));

    private static ServiceResponse ToResponse(CatalogDomainService service) =>
        new(
            service.ServiceId,
            service.TenantId,
            service.Name,
            service.Description,
            service.DurationMinutes,
            service.ReferencePrice,
            service.Modality,
            service.Status,
            service.CreatedAt,
            service.UpdatedAt);
}
