using System.Security.Claims;
using CatalogService.Common;
using CatalogService.Data;
using CatalogService.Domain;
using CatalogService.Features.Branches;
using CatalogService.Features.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class BranchesController(CatalogDbContext dbContext) : ControllerBase
{
    /// <summary>Lista las sucursales del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("branches")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BranchResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
        {
            return ValidationError("El estado debe ser 'active' o 'inactive'.");
        }

        var tenantId = GetTenantId();
        var query = dbContext.Branches
            .AsNoTracking()
            .Where(branch => branch.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(branch => branch.Status == status);
        }

        var branches = await query
            .OrderBy(branch => branch.Name)
            .Select(branch => ToResponse(branch))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BranchResponse>>.Ok(branches));
    }

    /// <summary>Obtiene una sucursal del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpGet("branches/{branchId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BranchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid branchId, CancellationToken cancellationToken)
    {
        var branch = await FindOwnedBranchAsync(branchId, true, cancellationToken);

        return branch is null
            ? BranchNotFound(branchId)
            : Ok(ApiResponse<BranchResponse>.Ok(ToResponse(branch)));
    }

    /// <summary>Crea una sucursal para el tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("branches")]
    [ProducesResponseType(typeof(ApiResponse<BranchResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateBranchRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var branch = new Branch
        {
            BranchId = Guid.NewGuid(),
            TenantId = GetTenantId(),
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Phone = request.Phone.Trim(),
            EmailContact = NormalizeOptional(request.EmailContact),
            Timezone = request.Timezone.Trim(),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Branches.Add(branch);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { branchId = branch.BranchId },
            ApiResponse<BranchResponse>.Ok(ToResponse(branch)));
    }

    /// <summary>Reemplaza los datos editables de una sucursal.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPut("branches/{branchId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BranchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid branchId,
        [FromBody] UpdateBranchRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await FindOwnedBranchAsync(branchId, false, cancellationToken);

        if (branch is null)
        {
            return BranchNotFound(branchId);
        }

        branch.Name = request.Name.Trim();
        branch.Address = request.Address.Trim();
        branch.Phone = request.Phone.Trim();
        branch.EmailContact = NormalizeOptional(request.EmailContact);
        branch.Timezone = request.Timezone.Trim();
        branch.Status = request.Status;
        branch.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<BranchResponse>.Ok(ToResponse(branch)));
    }

    /// <summary>Activa o desactiva una sucursal.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPatch("branches/{branchId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<BranchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(
        Guid branchId,
        [FromBody] UpdateBranchStatusRequest request,
        CancellationToken cancellationToken)
    {
        var branch = await FindOwnedBranchAsync(branchId, false, cancellationToken);

        if (branch is null)
        {
            return BranchNotFound(branchId);
        }

        branch.Status = request.Status;
        branch.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<BranchResponse>.Ok(ToResponse(branch)));
    }

    /// <summary>Realiza la baja logica de una sucursal.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("branches/{branchId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BranchResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid branchId, CancellationToken cancellationToken)
    {
        var branch = await FindOwnedBranchAsync(branchId, false, cancellationToken);

        if (branch is null)
        {
            return BranchNotFound(branchId);
        }

        branch.Status = "inactive";
        branch.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<BranchResponse>.Ok(ToResponse(branch)));
    }

    /// <summary>Habilita un servicio en una sucursal del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpPost("branches/{branchId:guid}/services/{serviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignService(
        Guid branchId,
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var branchExists = await dbContext.Branches
            .AsNoTracking()
            .AnyAsync(b => b.BranchId == branchId && b.TenantId == tenantId, cancellationToken);

        if (!branchExists)
            return BranchNotFound(branchId);

        var serviceExists = await dbContext.Services
            .AsNoTracking()
            .AnyAsync(s => s.ServiceId == serviceId && s.TenantId == tenantId, cancellationToken);

        if (!serviceExists)
            return ServiceNotFound(serviceId);

        var link = await dbContext.BranchServices
            .SingleOrDefaultAsync(
                bs => bs.BranchId == branchId && bs.ServiceId == serviceId,
                cancellationToken);

        if (link is not null)
        {
            if (link.Status == "active")
                return Ok();

            link.Status = "active";
            await dbContext.SaveChangesAsync(cancellationToken);
            return Ok();
        }

        dbContext.BranchServices.Add(new BranchService
        {
            TenantId = tenantId,
            BranchId = branchId,
            ServiceId = serviceId,
            Status = "active",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(StatusCodes.Status201Created);
    }

    /// <summary>Deshabilita un servicio en una sucursal del tenant autenticado.</summary>
    [Authorize(Policy = "TenantAdminOnly")]
    [HttpDelete("branches/{branchId:guid}/services/{serviceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveService(
        Guid branchId,
        Guid serviceId,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        var branchExists = await dbContext.Branches
            .AsNoTracking()
            .AnyAsync(b => b.BranchId == branchId && b.TenantId == tenantId, cancellationToken);

        if (!branchExists)
            return BranchNotFound(branchId);

        var link = await dbContext.BranchServices
            .SingleOrDefaultAsync(
                bs => bs.BranchId == branchId && bs.ServiceId == serviceId,
                cancellationToken);

        if (link is null || link.Status == "inactive")
            return ServiceNotFound(serviceId);

        link.Status = "inactive";
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>Lista las sucursales activas de un tenant activo para el portal publico.</summary>
    [AllowAnonymous]
    [HttpGet("public/tenants/{tenantSlug}/branches")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<BranchResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListPublic(
        string tenantSlug,
        CancellationToken cancellationToken)
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

        var branches = await dbContext.Branches
            .AsNoTracking()
            .Where(branch => branch.TenantId == tenantId.Value && branch.Status == "active")
            .OrderBy(branch => branch.Name)
            .Select(branch => ToResponse(branch))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<BranchResponse>>.Ok(branches));
    }

    /// <summary>Lista los servicios activos disponibles en una sucursal para el portal publico.</summary>
    [AllowAnonymous]
    [HttpGet("public/tenants/{tenantSlug}/branches/{branchId:guid}/services")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ServiceResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListServicesByBranchPublic(
        string tenantSlug,
        Guid branchId,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = tenantSlug.Trim().ToLowerInvariant();
        var tenantId = await dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Slug == normalizedSlug && t.Status == "active")
            .Select(t => (Guid?)t.TenantId)
            .SingleOrDefaultAsync(cancellationToken);

        if (!tenantId.HasValue)
        {
            return NotFound(ApiResponse<object>.Failure(
                "TENANT_NOT_FOUND",
                $"No existe un tenant activo con el slug '{normalizedSlug}'."));
        }

        var branchExists = await dbContext.Branches
            .AsNoTracking()
            .AnyAsync(
                b => b.BranchId == branchId && b.TenantId == tenantId.Value && b.Status == "active",
                cancellationToken);

        if (!branchExists)
            return BranchNotFound(branchId);

        var services = await dbContext.BranchServices
            .AsNoTracking()
            .Where(bs => bs.BranchId == branchId && bs.Status == "active")
            .Join(
                dbContext.Services.Where(s => s.Status == "active"),
                bs => bs.ServiceId,
                s => s.ServiceId,
                (bs, s) => s)
            .OrderBy(s => s.Name)
            .Select(s => ToServiceResponse(s))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<ServiceResponse>>.Ok(services));
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue("tenant_id")!);

    private Task<Branch?> FindOwnedBranchAsync(
        Guid branchId,
        bool asNoTracking,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var query = dbContext.Branches
            .Where(branch => branch.BranchId == branchId && branch.TenantId == tenantId);

        return (asNoTracking ? query.AsNoTracking() : query)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private NotFoundObjectResult BranchNotFound(Guid branchId) =>
        NotFound(ApiResponse<object>.Failure(
            "BRANCH_NOT_FOUND",
            $"No existe la sucursal '{branchId}' en el tenant autenticado."));

    private NotFoundObjectResult ServiceNotFound(Guid serviceId) =>
        NotFound(ApiResponse<object>.Failure(
            "SERVICE_NOT_FOUND",
            $"No existe el servicio '{serviceId}' en el tenant autenticado."));

    private BadRequestObjectResult ValidationError(string message) =>
        BadRequest(ApiResponse<object>.Failure("VALIDATION_ERROR", message));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static BranchResponse ToResponse(Branch branch) =>
        new(
            branch.BranchId,
            branch.TenantId,
            branch.Name,
            branch.Address,
            branch.Phone,
            branch.EmailContact,
            branch.Timezone,
            branch.Status,
            branch.CreatedAt,
            branch.UpdatedAt);

    private static ServiceResponse ToServiceResponse(Service service) =>
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
