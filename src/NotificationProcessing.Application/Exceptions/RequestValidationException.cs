namespace NotificationProcessing.Application.Exceptions;

/// <summary>
/// Raised when a request breaks a validation rule. Mapped to HTTP 400 (ValidationProblemDetails) by the API.
/// Keys are camelCase field names so they match the JSON the client sent.
/// </summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("One or more validation errors occurred.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
