namespace IdentityService.Features.Users;

public sealed record TenantAdminResponse(
    Guid UserId,
    Guid TenantId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    string Status,
    DateTimeOffset CreatedAt);
