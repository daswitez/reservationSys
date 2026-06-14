using System.ComponentModel.DataAnnotations;

namespace CatalogService.Features.Branches;

internal static class BranchRequestValidation
{
    public static IEnumerable<ValidationResult> ValidateTimezone(string timezone, string memberName)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            yield break;
        }

        var isValid = true;

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            isValid = false;
        }
        catch (InvalidTimeZoneException)
        {
            isValid = false;
        }

        if (!isValid)
        {
            yield return InvalidTimezone(memberName);
        }
    }

    public static IEnumerable<ValidationResult> ValidateStatus(string? status, string memberName)
    {
        if (!string.IsNullOrWhiteSpace(status) && status is not ("active" or "inactive"))
        {
            yield return new ValidationResult(
                "El estado debe ser 'active' o 'inactive'.",
                [memberName]);
        }
    }

    private static ValidationResult InvalidTimezone(string memberName) =>
        new("La zona horaria no es valida.", [memberName]);
}
