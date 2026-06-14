namespace IdentityService.Common;

public sealed record ApiError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? Details = null);
