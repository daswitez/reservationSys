using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace CatalogService.Features.ResourceSchedules;

internal static class ResourceScheduleRequestValidation
{
    private static readonly string[] TimeFormats = ["HH:mm", "HH:mm:ss"];
    private const string DateFormat = "yyyy-MM-dd";

    public static IEnumerable<ValidationResult> ValidateIds(Guid branchId, Guid resourceId)
    {
        if (branchId == Guid.Empty)
        {
            yield return new ValidationResult("La sucursal es obligatoria.", [nameof(branchId)]);
        }

        if (resourceId == Guid.Empty)
        {
            yield return new ValidationResult("El recurso es obligatorio.", [nameof(resourceId)]);
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

    public static IEnumerable<ValidationResult> ValidateTimeRange(
        string startTime,
        string endTime,
        string startMemberName,
        string endMemberName)
    {
        var startIsValid = TryParseTime(startTime, out var parsedStart);
        var endIsValid = TryParseTime(endTime, out var parsedEnd);

        if (!startIsValid)
        {
            yield return new ValidationResult(
                "La hora de inicio debe tener formato HH:mm.",
                [startMemberName]);
        }

        if (!endIsValid)
        {
            yield return new ValidationResult(
                "La hora de fin debe tener formato HH:mm.",
                [endMemberName]);
        }

        if (startIsValid && endIsValid && parsedEnd <= parsedStart)
        {
            yield return new ValidationResult(
                "La hora de fin debe ser mayor a la hora de inicio.",
                [endMemberName]);
        }
    }

    public static IEnumerable<ValidationResult> ValidateDateRange(
        string? validFrom,
        string? validTo,
        string validFromMemberName,
        string validToMemberName)
    {
        var fromIsValid = TryParseOptionalDate(validFrom, out var parsedFrom);
        var toIsValid = TryParseOptionalDate(validTo, out var parsedTo);

        if (!fromIsValid)
        {
            yield return new ValidationResult(
                "La fecha de inicio de vigencia debe tener formato yyyy-MM-dd.",
                [validFromMemberName]);
        }

        if (!toIsValid)
        {
            yield return new ValidationResult(
                "La fecha de fin de vigencia debe tener formato yyyy-MM-dd.",
                [validToMemberName]);
        }

        if (fromIsValid && toIsValid && parsedFrom.HasValue && parsedTo.HasValue && parsedTo < parsedFrom)
        {
            yield return new ValidationResult(
                "La fecha de fin de vigencia no puede ser menor a la fecha de inicio.",
                [validToMemberName]);
        }
    }

    public static bool TryParseTime(string? value, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            value,
            TimeFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    public static bool TryParseOptionalDate(string? value, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateOnly.TryParseExact(
                value,
                DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            date = parsed;
            return true;
        }

        return false;
    }
}
