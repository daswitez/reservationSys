namespace IdentityService.Domain;

public sealed class Role
{
    public short RoleId { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ICollection<UserRole> UserRoles { get; } = [];
}
