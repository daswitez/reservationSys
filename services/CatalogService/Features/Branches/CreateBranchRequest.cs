using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Branches;

public sealed class CreateBranchRequest : IValidatableObject
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "La direccion es obligatoria.")]
    [StringLength(500, MinimumLength = 3, ErrorMessage = "La direccion debe tener entre 3 y 500 caracteres.")]
    public string Address { get; init; } = string.Empty;

    [Required(ErrorMessage = "El telefono es obligatorio.")]
    [StringLength(40, MinimumLength = 5, ErrorMessage = "El telefono debe tener entre 5 y 40 caracteres.")]
    public string Phone { get; init; } = string.Empty;

    [EmailAddress(ErrorMessage = "El email de contacto no es valido.")]
    [StringLength(180, ErrorMessage = "El email de contacto no puede superar 180 caracteres.")]
    public string? EmailContact { get; init; }

    [Required(ErrorMessage = "La zona horaria es obligatoria.")]
    [StringLength(80, ErrorMessage = "La zona horaria no puede superar 80 caracteres.")]
    public string Timezone { get; init; } = string.Empty;

    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        BranchRequestValidation.ValidateTimezone(Timezone, nameof(Timezone))
            .Concat(BranchRequestValidation.ValidateStatus(Status, nameof(Status)));
}
