using System.Security.Claims;
using CatalogService.Common;
using CatalogService.Data;
using CatalogService.Features.Resources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CatalogDomainResource = CatalogService.Domain.Resource;

namespace CatalogService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ResourcesController(CatalogDbContext dbContext) : ControllerBase
{
    /// <summary>Lista los recursos reservables del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("resources")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ResourceResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? branchId,
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "blocked" or "inactive"))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "VALIDATION_ERROR",
                "El estado debe ser 'active', 'blocked' o 'inactive'."));
        }

        var tenantId = GetTenantId();
        var query = dbContext.Resources
            .AsNoTracking()
            .Where(resource => resource.TenantId == tenantId);

        if (branchId.HasValue)
        {
            query = query.Where(resource => resource.BranchId == branchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(resource => resource.Status == status);
        }

        var resources = await query
            .OrderBy(resource => resource.Name)
            .Select(resource => ToResponse(resource))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ResourceResponse>>.Ok(resources));
    }

    /// <summary>Obtiene un recurso reservable del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("resources/{resourceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid resourceId, CancellationToken cancellationToken)
    {
        var resource = await FindOwnedResourceAsync(resourceId, true, cancellationToken);

        return resource is null
            ? ResourceNotFound(resourceId)
            : Ok(ApiResponse<ResourceResponse>.Ok(ToResponse(resource)));
    }

    /// <summary>Crea un recurso reservable en una sucursal del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("resources")]
    [ProducesResponseType(typeof(ApiResponse<ResourceResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Create(
        [FromBody] CreateResourceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        if (!await BranchBelongsToTenantAsync(request.BranchId, tenantId, cancellationToken))
        {
            return BranchNotFound(request.BranchId);
        }

        var now = DateTimeOffset.UtcNow;
        var resource = new CatalogDomainResource
        {
            ResourceId = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = request.BranchId,
            Name = request.Name.Trim(),
            ResourceType = request.ResourceType.Trim().ToLowerInvariant(),
            Description = NormalizeOptional(request.Description),
            Capacity = request.Capacity,
            Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Resources.Add(resource);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { resourceId = resource.ResourceId },
            ApiResponse<ResourceResponse>.Ok(ToResponse(resource)));
    }

    /// <summary>Reemplaza los datos editables de un recurso reservable.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPut("resources/{resourceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid resourceId,
        [FromBody] UpdateResourceRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var resource = await FindOwnedResourceAsync(resourceId, false, cancellationToken);

        if (resource is null)
        {
            return ResourceNotFound(resourceId);
        }

        if (!await BranchBelongsToTenantAsync(request.BranchId, tenantId, cancellationToken))
        {
            return BranchNotFound(request.BranchId);
        }

        resource.BranchId = request.BranchId;
        resource.Name = request.Name.Trim();
        resource.ResourceType = request.ResourceType.Trim().ToLowerInvariant();
        resource.Description = NormalizeOptional(request.Description);
        resource.Capacity = request.Capacity;
        resource.Status = request.Status;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceResponse>.Ok(ToResponse(resource)));
    }

    /// <summary>Activa, bloquea o desactiva un recurso reservable.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPatch("resources/{resourceId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid resourceId,
        [FromBody] UpdateResourceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var resource = await FindOwnedResourceAsync(resourceId, false, cancellationToken);

        if (resource is null)
        {
            return ResourceNotFound(resourceId);
        }

        resource.Status = request.Status;
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceResponse>.Ok(ToResponse(resource)));
    }

    /// <summary>Realiza la baja logica de un recurso reservable.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("resources/{resourceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ResourceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid resourceId, CancellationToken cancellationToken)
    {
        var resource = await FindOwnedResourceAsync(resourceId, false, cancellationToken);

        if (resource is null)
        {
            return ResourceNotFound(resourceId);
        }

        resource.Status = "inactive";
        resource.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<ResourceResponse>.Ok(ToResponse(resource)));
    }

    private Guid GetTenantId() => Guid.Parse(User.FindFirstValue("tenant_id")!);

    private Task<CatalogDomainResource?> FindOwnedResourceAsync(
        Guid resourceId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = dbContext.Resources
            .Where(resource => resource.ResourceId == resourceId && resource.TenantId == tenantId);

        return (asNoTracking ? query.AsNoTracking() : query)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private Task<bool> BranchBelongsToTenantAsync(
        Guid branchId,
        Guid tenantId,
        CancellationToken cancellationToken) =>
        dbContext.Branches
            .AsNoTracking()
            .AnyAsync(branch => branch.BranchId == branchId && branch.TenantId == tenantId, cancellationToken);

    private NotFoundObjectResult ResourceNotFound(Guid resourceId) =>
        NotFound(ApiResponse<object>.Failure(
            "RESOURCE_NOT_FOUND",
            $"No existe el recurso '{resourceId}' en el tenant autenticado."));

    private NotFoundObjectResult BranchNotFound(Guid branchId) =>
        NotFound(ApiResponse<object>.Failure(
            "BRANCH_NOT_FOUND",
            $"No existe la sucursal '{branchId}' en el tenant autenticado."));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ResourceResponse ToResponse(CatalogDomainResource resource) =>
        new(
            resource.ResourceId,
            resource.TenantId,
            resource.BranchId,
            resource.Name,
            resource.ResourceType,
            resource.Description,
            resource.Capacity,
            resource.Status,
            resource.CreatedAt,
            resource.UpdatedAt);
}
