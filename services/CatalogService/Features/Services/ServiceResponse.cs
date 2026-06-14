namespace CatalogService.Features.Services;

public sealed record ServiceResponse(
    Guid ServiceId,
    Guid TenantId,
    string Name,
    string Description,
    int DurationMinutes,
    decimal? ReferencePrice,
    string Modality,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
