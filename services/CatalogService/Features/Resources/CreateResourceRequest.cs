using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Resources;

public sealed class CreateResourceRequest : IValidatableObject
{
    public Guid BranchId { get; init; }

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "El tipo de recurso es obligatorio.")]
    [StringLength(80, MinimumLength = 2, ErrorMessage = "El tipo de recurso debe tener entre 2 y 80 caracteres.")]
    public string ResourceType { get; init; } = string.Empty;

    [StringLength(2000, ErrorMessage = "La descripcion no puede superar 2000 caracteres.")]
    public string? Description { get; init; }

    [Range(1, int.MaxValue, ErrorMessage = "La capacidad debe ser mayor a 0.")]
    public int Capacity { get; init; } = 1;

    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ResourceRequestValidation.ValidateBranchId(BranchId, nameof(BranchId))
            .Concat(ResourceRequestValidation.ValidateStatus(Status, nameof(Status)));
}
