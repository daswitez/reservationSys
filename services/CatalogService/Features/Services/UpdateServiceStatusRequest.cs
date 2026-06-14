using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Services;

public sealed class UpdateServiceStatusRequest : IValidatableObject
{
    [Required(ErrorMessage = "El estado es obligatorio.")]
    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string Status { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ServiceRequestValidation.ValidateStatus(Status, nameof(Status));
}
