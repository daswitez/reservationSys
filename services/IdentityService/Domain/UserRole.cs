namespace IdentityService.Domain;

public sealed class UserRole
{
    public Guid UserId { get; set; }
    public short RoleId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}
