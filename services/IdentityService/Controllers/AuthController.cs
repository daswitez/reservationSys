using System.Security.Claims;
using IdentityService.Authorization;
using IdentityService.Common;
using IdentityService.Data;
using IdentityService.Domain;
using IdentityService.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IdentityService.Controllers;

[ApiController]
[Route("auth")]
[Produces("application/json")]
public sealed class AuthController(
    IdentityDbContext dbContext,
    JwtTokenService jwtTokenService,
    IAuthorizationService authorizationService) : ControllerBase
{
    private const string ClientRole = "client";

    [AllowAnonymous]
    [HttpPost("register-client")]
    [ProducesResponseType(typeof(ApiResponse<RegisterClientResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RegisterClient(
        [FromBody] RegisterClientRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await dbContext.Users.AnyAsync(
                user => user.Email.ToLower() == email,
                cancellationToken))
        {
            return ClientEmailConflict(email);
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(
            candidate => candidate.Code == ClientRole,
            cancellationToken);

        if (role is null)
        {
            throw new InvalidOperationException($"The required role '{ClientRole}' is not configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            UserId = Guid.NewGuid(),
            TenantId = null,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName.Trim(),
            LastName = NullIfWhiteSpace(request.LastName),
            Phone = NullIfWhiteSpace(request.Phone),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.UserRoles.Add(new UserRole
        {
            User = user,
            Role = role,
            CreatedAt = now
        });
        dbContext.Users.Add(user);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return ClientEmailConflict(email);
        }

        var response = new RegisterClientResponse(
            user.UserId,
            user.Email,
            [ClientRole],
            user.Status,
            user.CreatedAt);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<RegisterClientResponse>.Ok(response));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var candidates = await dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Where(user => user.Email.ToLower() == email)
            .ToListAsync(cancellationToken);

        var matches = candidates
            .Where(user => PasswordMatches(request.Password, user.PasswordHash))
            .ToList();

        if (matches.Count != 1 || matches[0].Status != "active")
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "INVALID_CREDENTIALS",
                "El email o la contrasena son incorrectos."));
        }

        var user = matches[0];
        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (roles.Length == 0)
        {
            return Unauthorized(ApiResponse<object>.Failure(
                "INVALID_CREDENTIALS",
                "El email o la contrasena son incorrectos."));
        }

        var token = jwtTokenService.Create(user, roles);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.UpdatedAt = user.LastLoginAt.Value;
        await dbContext.SaveChangesAsync(cancellationToken);

        var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var response = new LoginResponse(
            token.AccessToken,
            token.ExpiresIn,
            new AuthenticatedUserResponse(
                user.UserId,
                user.TenantId,
                user.Email,
                fullName,
                roles));

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(ApiResponse<LoginResponse>.Ok(response));
    }

    [Authorize(Policy = "AuthenticatedUser")]
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!TryGetGuidClaim("user_id", out var userId))
        {
            return Forbid();
        }

        var user = await dbContext.Users
            .Include(candidate => candidate.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (user is null || user.Status != "active" || !TenantClaimMatches(user.TenantId))
        {
            return Forbid();
        }

        var roles = user.UserRoles
            .Select(userRole => userRole.Role.Code)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var response = new CurrentUserResponse(
            user.UserId,
            user.TenantId,
            user.Email,
            fullName,
            roles,
            user.Status);

        return Ok(ApiResponse<CurrentUserResponse>.Ok(response));
    }

    [Authorize(Policy = "AdministrativePanel")]
    [HttpGet("access/branches/{branchId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<BranchAccessResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckBranchAccess(Guid branchId)
    {
        var authorizationResult = await authorizationService.AuthorizeAsync(
            User,
            branchId,
            BranchAccessRequirement.Instance);

        if (!authorizationResult.Succeeded ||
            !TryGetGuidClaim("user_id", out var userId) ||
            !TryGetGuidClaim("tenant_id", out var tenantId))
        {
            return Forbid();
        }

        return Ok(ApiResponse<BranchAccessResponse>.Ok(new BranchAccessResponse(
            userId,
            tenantId,
            branchId,
            true)));
    }

    private static bool PasswordMatches(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }

    private ConflictObjectResult ClientEmailConflict(string email) =>
        Conflict(ApiResponse<object>.Failure(
            "USER_EMAIL_ALREADY_EXISTS",
            $"Ya existe un usuario con el email '{email}'."));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool TryGetGuidClaim(string claimType, out Guid value) =>
        Guid.TryParse(User.FindFirstValue(claimType), out value);

    private bool TenantClaimMatches(Guid? userTenantId)
    {
        var tenantClaim = User.FindFirstValue("tenant_id");

        return userTenantId.HasValue
            ? Guid.TryParse(tenantClaim, out var tenantId) && tenantId == userTenantId.Value
            : string.IsNullOrWhiteSpace(tenantClaim);
    }
}
