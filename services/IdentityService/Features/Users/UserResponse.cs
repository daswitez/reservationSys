namespace IdentityService.Features.Users;

public sealed record UserResponse(
    Guid UserId,
    Guid? TenantId,
    string Email,
    string FirstName,
    string? LastName,
    string? Phone,
    IReadOnlyList<string> Roles,
    IReadOnlyList<Guid> BranchIds,
    string Status,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UserListResponse(
    IReadOnlyList<UserResponse> Items,
    int Total);
