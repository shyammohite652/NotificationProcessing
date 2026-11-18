using Microsoft.AspNetCore.Mvc;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Services;

namespace NotificationProcessing.API.Controllers;

/// <summary>Thin controller: HTTP concerns only (routes, status codes, headers). Rules live in the services.</summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
public sealed class NotificationsController(NotificationService notificationService, NotificationProcessor processor) : ControllerBase
{
    public const string DuplicateHeader = "X-Duplicate-Request";

    /// <summary>Accepts a notification request.</summary>
    /// <remarks>
    /// Returns 201 for a new notification. If an identical request was accepted within the duplicate window,
    /// returns 200 with the original notification and the header <c>X-Duplicate-Request: true</c>, so a client
    /// that retried after a timeout simply gets the result it missed.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NotificationResponse>> Create(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var result = await notificationService.CreateAsync(request, cancellationToken);
        var location = Url.Action(nameof(GetById), new { id = result.Notification.Id });

        if (result.IsDuplicate)
        {
            Response.Headers[DuplicateHeader] = "true";
            Response.Headers.Location = location;
            return Ok(result.Notification);
        }

        return Created(location, result.Notification);
    }

    /// <summary>Gets a notification by its ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<NotificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var notification = await notificationService.GetByIdAsync(id, cancellationToken);
        return notification is null ? NotFound() : Ok(notification);
    }

    /// <summary>Processes due Pending notifications: High first, then Normal, then Low; FIFO within a priority.</summary>
    /// <remarks>Safe to call concurrently: each notification is claimed atomically by exactly one run.</remarks>
    [HttpPost("process")]
    [ProducesResponseType<ProcessNotificationsResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProcessNotificationsResult>> Process(CancellationToken cancellationToken) =>
        Ok(await processor.ProcessPendingAsync(cancellationToken));
}
