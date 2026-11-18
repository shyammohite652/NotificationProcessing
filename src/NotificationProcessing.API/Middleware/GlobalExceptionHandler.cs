using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotificationProcessing.Application.Exceptions;

namespace NotificationProcessing.API.Middleware;

/// <summary>
/// Central place that turns exceptions into RFC 7807 problem responses, so controllers contain no try/catch.
/// Validation errors → 400 ValidationProblemDetails; anything unexpected → 500 without internal details.
/// </summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;

        switch (exception)
        {
            case RequestValidationException validation:
                problem = new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred."
                };
                break;

            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                // Client disconnected; nothing useful to send. 499 is the de-facto "client closed request" code.
                logger.LogInformation("Request {Path} was cancelled by the client", httpContext.Request.Path);
                httpContext.Response.StatusCode = 499;
                return true;

            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "The error has been logged. Please retry later or contact support with the traceId."
                };
                break;
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
