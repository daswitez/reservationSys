using System.ComponentModel.DataAnnotations;

namespace IdentityService.Features.Users;

public sealed class CreateTenantAdminRequest : IValidatableObject
{
    public Guid TenantId { get; init; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 120 caracteres.")]
    public string FirstName { get; init; } = string.Empty;

    [StringLength(120, ErrorMessage = "El apellido no puede superar 120 caracteres.")]
    public string? LastName { get; init; }

    [Required(ErrorMessage = "El email es obligatorio.")]
    [EmailAddress(ErrorMessage = "El email no es valido.")]
    [StringLength(180, ErrorMessage = "El email no puede superar 180 caracteres.")]
    public string Email { get; init; } = string.Empty;

    [StringLength(40, ErrorMessage = "El telefono no puede superar 40 caracteres.")]
    public string? Phone { get; init; }

    [Required(ErrorMessage = "La contrasena es obligatoria.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "La contrasena debe tener entre 8 y 128 caracteres.")]
    public string Password { get; init; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TenantId == Guid.Empty)
        {
            yield return new ValidationResult(
                "El tenant es obligatorio.",
                [nameof(TenantId)]);
        }
    }
}
