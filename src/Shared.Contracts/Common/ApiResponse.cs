using System.Diagnostics;

namespace SwiftRide.Contracts.Common;

public record ApiError(string Code, string Message, string? Field = null);

public record ApiResponse<T>(
    int StatusCode,
    string Message,
    T? Data,
    object? Meta = null,
    List<ApiError>? Errors = null,
    string? TraceId = null)
{
    public static ApiResponse<T> Ok(T data, string message = "Success", string? traceId = null)
        => new(200, message, data, null, null, traceId ?? Activity.Current?.Id);

    public static ApiResponse<T> Created(T data, string message = "Created", string? traceId = null)
        => new(201, message, data, null, null, traceId ?? Activity.Current?.Id);

    public static ApiResponse<T> Fail(int statusCode, string code, string message,
        string? field = null, string? traceId = null)
        => new(statusCode, message, default, null, [new ApiError(code, message, field)], traceId ?? Activity.Current?.Id);
}
