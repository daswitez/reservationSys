using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Services;

public sealed class CreateServiceRequest : IValidatableObject
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "La descripcion es obligatoria.")]
    [StringLength(2000, MinimumLength = 2, ErrorMessage = "La descripcion debe tener entre 2 y 2000 caracteres.")]
    public string Description { get; init; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "La duracion debe ser mayor a 0 minutos.")]
    public int DurationMinutes { get; init; }

    [Range(typeof(decimal), "0", "99999999.99", ErrorMessage = "El precio referencial debe estar entre 0 y 99999999.99.")]
    public decimal? ReferencePrice { get; init; }

    [Required(ErrorMessage = "La modalidad es obligatoria.")]
    [StringLength(30, MinimumLength = 2, ErrorMessage = "La modalidad debe tener entre 2 y 30 caracteres.")]
    public string Modality { get; init; } = string.Empty;

    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ServiceRequestValidation.ValidateStatus(Status, nameof(Status));
}
