namespace IdentityService.Features.Auth;

public sealed record RegisterClientResponse(
    Guid UserId,
    string Email,
    IReadOnlyList<string> Roles,
    string Status,
    DateTimeOffset CreatedAt);
