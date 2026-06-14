namespace IdentityService.Features.Auth;

public sealed record LoginResponse(
    string AccessToken,
    int ExpiresIn,
    AuthenticatedUserResponse User);

public sealed record AuthenticatedUserResponse(
    Guid UserId,
    Guid? TenantId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
