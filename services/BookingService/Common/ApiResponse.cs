namespace BookingService.Common;

public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiError? Error)
{
    public static ApiResponse<T> Ok(T data) => new(true, data, null);

    public static ApiResponse<T> Failure(
        string code,
        string message,
        object? details = null) =>
        new(false, default, new ApiError(code, message, details));
}
