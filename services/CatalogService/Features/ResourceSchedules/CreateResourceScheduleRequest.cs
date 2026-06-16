using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.ResourceSchedules;

public sealed class CreateResourceScheduleRequest : IValidatableObject
{
    public Guid BranchId { get; init; }
    public Guid ResourceId { get; init; }

    [Range(1, 7, ErrorMessage = "El dia de semana debe estar entre 1 y 7.")]
    public short DayOfWeek { get; init; }

    [Required(ErrorMessage = "La hora de inicio es obligatoria.")]
    public string StartTime { get; init; } = string.Empty;

    [Required(ErrorMessage = "La hora de fin es obligatoria.")]
    public string EndTime { get; init; } = string.Empty;

    public string? ValidFrom { get; init; }
    public string? ValidTo { get; init; }

    [StringLength(30, ErrorMessage = "El estado no puede superar 30 caracteres.")]
    public string? Status { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ResourceScheduleRequestValidation.ValidateIds(BranchId, ResourceId)
            .Concat(ResourceScheduleRequestValidation.ValidateTimeRange(
                StartTime,
                EndTime,
                nameof(StartTime),
                nameof(EndTime)))
            .Concat(ResourceScheduleRequestValidation.ValidateDateRange(
                ValidFrom,
                ValidTo,
                nameof(ValidFrom),
                nameof(ValidTo)))
            .Concat(ResourceScheduleRequestValidation.ValidateStatus(Status, nameof(Status)));
}
