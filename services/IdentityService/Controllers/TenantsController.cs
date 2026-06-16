using IdentityService.Common;
using IdentityService.Data;
using IdentityService.Domain;
using IdentityService.Features.Tenants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IdentityService.Controllers;

[ApiController]
[Route("tenants")]
[Produces("application/json")]
public sealed class TenantsController(IdentityDbContext dbContext) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("public")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<TenantResponse>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPublic(CancellationToken cancellationToken)
    {
        var tenants = await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Status == "active")
            .OrderBy(tenant => tenant.Name)
            .Select(tenant => new TenantResponse(
                tenant.TenantId,
                tenant.Name,
                tenant.Slug,
                tenant.MainCategory,
                tenant.Timezone,
                tenant.Status,
                tenant.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<IReadOnlyList<TenantResponse>>.Ok(tenants));
    }

    [AllowAnonymous]
    [HttpGet("public/{slug}")]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPublicBySlug(
        string slug,
        CancellationToken cancellationToken)
    {
        var normalizedSlug = slug.Trim().ToLowerInvariant();
        var tenant = await dbContext.Tenants
            .AsNoTracking()
            .Where(tenant => tenant.Slug == normalizedSlug && tenant.Status == "active")
            .Select(tenant => new TenantResponse(
                tenant.TenantId,
                tenant.Name,
                tenant.Slug,
                tenant.MainCategory,
                tenant.Timezone,
                tenant.Status,
                tenant.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

        if (tenant is null)
        {
            return NotFound(ApiResponse<object>.Failure(
                "TENANT_NOT_FOUND",
                $"No existe un tenant activo con el slug '{normalizedSlug}'."));
        }

        return Ok(ApiResponse<TenantResponse>.Ok(tenant));
    }

    /// <summary>
    /// Registra una empresa en la plataforma.
    /// </summary>
    /// <remarks>
    /// Requiere un JWT con el rol super_admin. El estado es active cuando no se envia.
    /// </remarks>
    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<TenantResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] CreateTenantRequest request,
        CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim();

        if (await dbContext.Tenants.AnyAsync(tenant => tenant.Slug == slug, cancellationToken))
        {
            return TenantSlugConflict(slug);
        }

        var now = DateTimeOffset.UtcNow;
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = slug,
            MainCategory = request.MainCategory.Trim(),
            Timezone = request.Timezone.Trim(),
            Status = string.IsNullOrWhiteSpace(request.Status) ? "active" : request.Status,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Tenants.Add(tenant);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return TenantSlugConflict(slug);
        }

        var response = new TenantResponse(
            tenant.TenantId,
            tenant.Name,
            tenant.Slug,
            tenant.MainCategory,
            tenant.Timezone,
            tenant.Status,
            tenant.CreatedAt);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<TenantResponse>.Ok(response));
    }

    private ConflictObjectResult TenantSlugConflict(string slug) =>
        Conflict(ApiResponse<object>.Failure(
            "TENANT_SLUG_ALREADY_EXISTS",
            $"Ya existe una empresa con el slug '{slug}'."));
}
