namespace IdentityService.Domain;

public sealed class Tenant
{
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public required string MainCategory { get; set; }
    public required string Timezone { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
