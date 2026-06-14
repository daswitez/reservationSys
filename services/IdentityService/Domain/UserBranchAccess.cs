namespace IdentityService.Domain;

public sealed class UserBranchAccess
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public User User { get; set; } = null!;
}
