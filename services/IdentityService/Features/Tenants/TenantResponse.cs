namespace IdentityService.Features.Tenants;

public sealed record TenantResponse(
    Guid TenantId,
    string Name,
    string Slug,
    string MainCategory,
    string Timezone,
    string Status,
    DateTimeOffset CreatedAt);
