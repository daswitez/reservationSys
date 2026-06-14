using System.Security.Claims;
using IdentityService.Common;
using IdentityService.Data;
using IdentityService.Domain;
using IdentityService.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IdentityService.Controllers;

[ApiController]
[Route("users")]
[Produces("application/json")]
public sealed class UsersController(IdentityDbContext dbContext) : ControllerBase
{
    private const string TenantAdminRole = "tenant_admin";

    [Authorize(Policy = "UserAdministration")]
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<UserListResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListUsers(
        [FromQuery] Guid? tenantId,
        [FromQuery] string? role,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            return ValidationFailure("offset", "El desplazamiento no puede ser negativo.");
        }

        if (limit is < 1 or > 200)
        {
            return ValidationFailure("limit", "El limite debe estar entre 1 y 200.");
        }

        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive" or "blocked"))
        {
            return ValidationFailure("status", "El estado no es valido.");
        }

        var query = dbContext.Users
            .AsNoTracking()
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Include(user => user.BranchAccess)
            .AsQueryable();

        if (User.IsInRole("super_admin"))
        {
            if (tenantId.HasValue)
            {
                query = query.Where(user => user.TenantId == tenantId.Value);
            }
        }
        else if (TryGetTenantId(out var currentTenantId))
        {
            if (tenantId.HasValue && tenantId.Value != currentTenantId)
            {
                return Forbid();
            }

            query = query.Where(user => user.TenantId == currentTenantId);
        }
        else
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalizedRole = role.Trim().ToLowerInvariant();
            query = query.Where(user => user.UserRoles.Any(userRole => userRole.Role.Code == normalizedRole));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(user => user.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(user =>
                user.Email.ToLower().Contains(normalizedSearch) ||
                user.FirstName.ToLower().Contains(normalizedSearch) ||
                (user.LastName != null && user.LastName.ToLower().Contains(normalizedSearch)));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.Email)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<UserListResponse>.Ok(new UserListResponse(
            users.Select(ToResponse).ToArray(),
            total)));
    }

    [Authorize(Policy = "UserAdministration")]
    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUser(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserNotFound();
        }

        if (!CanManage(user))
        {
            return Forbid();
        }

        return Ok(ApiResponse<UserResponse>.Ok(ToResponse(user)));
    }

    [Authorize(Policy = "SuperAdminOnly")]
    [HttpPost("admin")]
    [ProducesResponseType(typeof(ApiResponse<TenantAdminResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateTenantAdmin(
        [FromBody] CreateTenantAdminRequest request,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Tenants.AnyAsync(
                tenant => tenant.TenantId == request.TenantId,
                cancellationToken))
        {
            return NotFound(ApiResponse<object>.Failure(
                "TENANT_NOT_FOUND",
                "La empresa indicada no existe."));
        }

        var email = request.Email.Trim().ToLowerInvariant();

        if (await dbContext.Users.AnyAsync(
                user => user.Email.ToLower() == email,
                cancellationToken))
        {
            return EmailConflict(email);
        }

        var role = await dbContext.Roles.SingleOrDefaultAsync(
            role => role.Code == TenantAdminRole,
            cancellationToken);

        if (role is null)
        {
            throw new InvalidOperationException($"The required role '{TenantAdminRole}' is not configured.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            UserId = Guid.NewGuid(),
            TenantId = request.TenantId,
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
            return EmailConflict(email);
        }

        var fullName = string.Join(' ', new[] { user.FirstName, user.LastName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var response = new TenantAdminResponse(
            user.UserId,
            request.TenantId,
            user.Email,
            fullName,
            [TenantAdminRole],
            user.Status,
            user.CreatedAt);

        return StatusCode(StatusCodes.Status201Created, ApiResponse<TenantAdminResponse>.Ok(response));
    }

    [Authorize(Policy = "UserAdministration")]
    [HttpPut("{userId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateUser(
        Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserNotFound();
        }

        if (!CanManage(user))
        {
            return Forbid();
        }

        return await ApplyProfileUpdateAsync(user, request, cancellationToken);
    }

    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPut("me")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOwnProfile(
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        var user = await FindUserAsync(userId, cancellationToken);

        if (user is null || user.Status != "active")
        {
            return Forbid();
        }

        return await ApplyProfileUpdateAsync(user, request, cancellationToken);
    }

    [Authorize(Policy = "AuthenticatedUser")]
    [HttpPatch("me/password")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChangeOwnPassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId))
        {
            return Forbid();
        }

        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.UserId == userId,
            cancellationToken);

        if (user is null || user.Status != "active")
        {
            return Forbid();
        }

        if (!PasswordMatches(request.CurrentPassword, user.PasswordHash))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "CURRENT_PASSWORD_INVALID",
                "La contrasena actual es incorrecta."));
        }

        if (PasswordMatches(request.NewPassword, user.PasswordHash))
        {
            return BadRequest(ApiResponse<object>.Failure(
                "PASSWORD_REUSE_NOT_ALLOWED",
                "La nueva contrasena debe ser diferente de la actual."));
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.AuthVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ApiResponse<object>.Ok(new
        {
            message = "Contrasena actualizada. Los tokens anteriores dejaron de ser validos."
        }));
    }

    [Authorize(Policy = "UserAdministration")]
    [HttpPatch("{userId:guid}/status")]
    [ProducesResponseType(typeof(ApiResponse<UserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserStatus(
        Guid userId,
        [FromBody] UpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserNotFound();
        }

        if (!CanManage(user) || IsDisablingCurrentUser(userId, request.Status))
        {
            return Forbid();
        }

        if (user.Status != request.Status)
        {
            user.Status = request.Status;
            user.AuthVersion++;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(ApiResponse<UserResponse>.Ok(ToResponse(user)));
    }

    [Authorize(Policy = "UserAdministration")]
    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(userId, cancellationToken);

        if (user is null)
        {
            return UserNotFound();
        }

        if (!CanManage(user) || IsDisablingCurrentUser(userId, "inactive"))
        {
            return Forbid();
        }

        if (user.Status != "inactive")
        {
            user.Status = "inactive";
            user.AuthVersion++;
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<IActionResult> ApplyProfileUpdateAsync(
        User user,
        UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await dbContext.Users.AnyAsync(
                candidate => candidate.UserId != user.UserId && candidate.Email.ToLower() == email,
                cancellationToken))
        {
            return EmailConflict(email);
        }

        var emailChanged = user.Email != email;
        user.Email = email;
        user.FirstName = request.FirstName.Trim();
        user.LastName = NullIfWhiteSpace(request.LastName);
        user.Phone = NullIfWhiteSpace(request.Phone);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        if (emailChanged)
        {
            user.AuthVersion++;
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return EmailConflict(email);
        }

        return Ok(ApiResponse<UserResponse>.Ok(ToResponse(user)));
    }

    private Task<User?> FindUserAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .Include(user => user.BranchAccess)
            .SingleOrDefaultAsync(user => user.UserId == userId, cancellationToken);

    private bool CanManage(User target)
    {
        if (User.IsInRole("super_admin"))
        {
            return true;
        }

        return User.IsInRole("tenant_admin") &&
               TryGetTenantId(out var tenantId) &&
               target.TenantId == tenantId &&
               target.UserRoles.All(userRole => userRole.Role.Code != "super_admin");
    }

    private bool IsDisablingCurrentUser(Guid targetUserId, string status) =>
        status != "active" && TryGetUserId(out var currentUserId) && targetUserId == currentUserId;

    private bool TryGetUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirstValue("user_id"), out userId);

    private bool TryGetTenantId(out Guid tenantId) =>
        Guid.TryParse(User.FindFirstValue("tenant_id"), out tenantId);

    private static UserResponse ToResponse(User user) => new(
        user.UserId,
        user.TenantId,
        user.Email,
        user.FirstName,
        user.LastName,
        user.Phone,
        user.UserRoles.Select(userRole => userRole.Role.Code).Order(StringComparer.Ordinal).ToArray(),
        user.BranchAccess.Select(access => access.BranchId).Order().ToArray(),
        user.Status,
        user.LastLoginAt,
        user.CreatedAt,
        user.UpdatedAt);

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

    private NotFoundObjectResult UserNotFound() =>
        NotFound(ApiResponse<object>.Failure(
            "USER_NOT_FOUND",
            "El usuario indicado no existe."));

    private BadRequestObjectResult ValidationFailure(string field, string message) =>
        BadRequest(ApiResponse<object>.Failure(
            "VALIDATION_ERROR",
            "La solicitud contiene datos invalidos.",
            new Dictionary<string, string[]> { [field] = [message] }));

    private ConflictObjectResult EmailConflict(string email) =>
        Conflict(ApiResponse<object>.Failure(
            "USER_EMAIL_ALREADY_EXISTS",
            $"Ya existe un usuario con el email '{email}'."));

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
