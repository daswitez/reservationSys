namespace IdentityService.Domain;

public sealed class User
{
    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public required string FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public required string Status { get; set; }
    public int AuthVersion { get; set; } = 1;
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Tenant? Tenant { get; set; }
    public ICollection<UserRole> UserRoles { get; } = [];
    public ICollection<UserBranchAccess> BranchAccess { get; } = [];
}
