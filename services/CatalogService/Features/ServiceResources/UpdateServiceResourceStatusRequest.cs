using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.ServiceResources;

public sealed class UpdateServiceResourceStatusRequest : IValidatableObject
{
    [Required(ErrorMessage = "El estado es obligatorio.")]
    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string Status { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is not ("active" or "inactive"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active' o 'inactive'.",
                [nameof(Status)]);
        }
    }
}
