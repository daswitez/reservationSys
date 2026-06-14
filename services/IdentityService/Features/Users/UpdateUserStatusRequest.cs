using System.ComponentModel.DataAnnotations;

namespace IdentityService.Features.Users;

public sealed class UpdateUserStatusRequest : IValidatableObject
{
    [Required(ErrorMessage = "El estado es obligatorio.")]
    public string Status { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status is not ("active" or "inactive" or "blocked"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active', 'inactive' o 'blocked'.",
                [nameof(Status)]);
        }
    }
}
