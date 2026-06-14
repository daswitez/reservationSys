namespace IdentityService.Features.Auth;

public sealed record CurrentUserResponse(
    Guid UserId,
    Guid? TenantId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    string Status);

public sealed record BranchAccessResponse(
    Guid UserId,
    Guid TenantId,
    Guid BranchId,
    bool Allowed);
