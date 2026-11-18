using Microsoft.AspNetCore.Mvc;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Services;

namespace NotificationProcessing.API.Controllers;

[ApiController]
[Route("api/customers/{customerId}/notifications")]
[Produces("application/json")]
public sealed class CustomerNotificationsController(NotificationService notificationService) : ControllerBase
{
    /// <summary>Lists a customer's notifications, newest first.</summary>
    /// <param name="customerId">Customer identifier, e.g. CUST-101.</param>
    /// <param name="status">Optional filter: Pending, Processing, Sent or Failed.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1–100).</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<PagedResponse<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<NotificationResponse>>> Get(
        string customerId,
        [FromQuery] string? status,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = NotificationService.DefaultPageSize) =>
        Ok(await notificationService.GetForCustomerAsync(customerId, status, page, pageSize, cancellationToken));
}
