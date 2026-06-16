using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Resources;

internal static class ResourceRequestValidation
{
    public static IEnumerable<ValidationResult> ValidateStatus(string? status, string memberName)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "blocked" or "inactive"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active', 'blocked' o 'inactive'.",
                [memberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateBranchId(Guid branchId, string memberName)
    {
        if (branchId == Guid.Empty)
        {
            yield return new ValidationResult(
                "La sucursal es obligatoria.",
                [memberName]);
        }
    }
}
