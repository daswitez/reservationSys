using System.ComponentModel.DataAnnotations;

namespace IdentityService.Features.Tenants;

public sealed class CreateTenantRequest : IValidatableObject
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(150, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 150 caracteres.")]
    public string Name { get; init; } = string.Empty;

    [Required(ErrorMessage = "El slug es obligatorio.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "El slug debe tener entre 2 y 120 caracteres.")]
    [RegularExpression(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        ErrorMessage = "El slug solo puede contener letras minusculas, numeros y guiones simples.")]
    public string Slug { get; init; } = string.Empty;

    [Required(ErrorMessage = "El rubro principal es obligatorio.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "El rubro principal debe tener entre 2 y 120 caracteres.")]
    public string MainCategory { get; init; } = string.Empty;

    [Required(ErrorMessage = "La zona horaria es obligatoria.")]
    [StringLength(80, ErrorMessage = "La zona horaria no puede superar 80 caracteres.")]
    public string Timezone { get; init; } = string.Empty;

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

        if (string.IsNullOrWhiteSpace(Timezone))
        {
            yield break;
        }

        var isValidTimezone = true;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            isValidTimezone = false;
        }
        catch (InvalidTimeZoneException)
        {
            isValidTimezone = false;
        }

        if (!isValidTimezone)
        {
            yield return new ValidationResult(
                "La zona horaria no es valida.",
                [nameof(Timezone)]);
        }
    }
}
