using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Services;

internal static class ServiceRequestValidation
{
    public static IEnumerable<ValidationResult> ValidateStatus(string? status, string memberName)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active' o 'inactive'.",
                [memberName]);
        }
    }
}
