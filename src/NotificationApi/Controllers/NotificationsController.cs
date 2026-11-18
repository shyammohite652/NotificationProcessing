using Microsoft.AspNetCore.Mvc;
using NotificationApi.Models;
using NotificationApi.Services;

namespace NotificationApi.Controllers;

[ApiController]
[Route("api")]
public class NotificationsController(NotificationService service, NotificationProcessor processor) : ControllerBase
{
    /// <summary>Create a notification. 201 if new; 200 with the original if it is a duplicate.</summary>
    [HttpPost("notifications")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateNotificationRequest request, CancellationToken ct)
    {
        var (notification, isDuplicate) = await service.CreateAsync(request, ct);

        if (isDuplicate)
        {
            Response.Headers["X-Duplicate-Request"] = "true";
            return Ok(notification);
        }

        return CreatedAtAction(nameof(GetById), new { id = notification.Id }, notification);
    }

    [HttpGet("notifications/{id:guid}")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var notification = await service.GetByIdAsync(id, ct);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>A customer's notifications, newest first.</summary>
    [HttpGet("customers/{customerId}/notifications")]
    [ProducesResponseType<PagedResponse<NotificationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetForCustomer(string customerId, CancellationToken ct, int page = 1, int pageSize = 20) =>
        Ok(await service.GetForCustomerAsync(customerId, page, pageSize, ct));

    /// <summary>Send due Pending notifications: High first, then Normal, then Low; oldest first within a priority.</summary>
    [HttpPost("notifications/process")]
    [ProducesResponseType<ProcessResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Process(CancellationToken ct) =>
        Ok(await processor.ProcessPendingAsync(ct));
}
