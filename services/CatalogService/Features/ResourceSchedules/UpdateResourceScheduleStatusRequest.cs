using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.ResourceSchedules;

public sealed class UpdateResourceScheduleStatusRequest : IValidatableObject
{
    [Required(ErrorMessage = "El estado es obligatorio.")]
    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string Status { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ResourceScheduleRequestValidation.ValidateStatus(Status, nameof(Status));
}
