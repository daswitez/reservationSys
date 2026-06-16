using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.ServiceResources;

public sealed class UpsertServiceResourceRequest : IValidatableObject
{
    public bool Required { get; init; } = true;

    [Range(1, int.MaxValue, ErrorMessage = "La prioridad debe ser mayor a 0.")]
    public int Priority { get; init; } = 1;

    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Status) && Status is not ("active" or "inactive"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active' o 'inactive'.",
                [nameof(Status)]);
        }
    }
}
