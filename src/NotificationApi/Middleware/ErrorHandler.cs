using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using NotificationApi.Services;

namespace NotificationApi.Middleware;

// Turns exceptions into consistent ProblemDetails responses: validation → 400, anything else → 500.
public class ErrorHandler(IProblemDetailsService problemDetails, ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem;
        if (exception is ValidationException validation)
        {
            problem = new ValidationProblemDetails(validation.Errors) { Status = StatusCodes.Status400BadRequest };
        }
        else
        {
            logger.LogError(exception, "Unhandled exception");
            problem = new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "An unexpected error occurred." };
        }

        context.Response.StatusCode = problem.Status.Value;
        return await problemDetails.TryWriteAsync(new() { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
