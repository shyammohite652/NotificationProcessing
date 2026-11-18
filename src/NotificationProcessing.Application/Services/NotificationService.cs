using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Exceptions;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Application.Validators;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;

namespace NotificationProcessing.Application.Services;

/// <summary>Accepting and reading notifications. Processing lives in <see cref="NotificationProcessor"/>.</summary>
public sealed class NotificationService(
    INotificationRepository repository,
    CreateNotificationRequestValidator validator,
    TimeProvider timeProvider,
    IOptions<NotificationOptions> options,
    ILogger<NotificationService> logger)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    private readonly NotificationOptions _options = options.Value;

    public async Task<CreateNotificationResult> CreateAsync(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        var errors = validator.Validate(request, now);
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        // Safe after validation.
        var customerId = request.CustomerId!.Trim();
        var recipient = request.Recipient!.Trim();
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim();
        var message = request.Message!;
        EnumParser.TryParseName<NotificationType>(request.Type, out var type);
        var priority = NotificationPriority.Normal; // default when omitted
        if (request.Priority is not null)
        {
            EnumParser.TryParseName(request.Priority, out priority);
        }

        var fingerprint = NotificationFingerprint.Compute(
            customerId, type, recipient, subject, message, priority, request.RequestedDeliveryTime);

        // 1) Application-level check: sliding window "was this exact request accepted in the last N minutes?"
        var existing = await repository.FindRecentByFingerprintAsync(
            fingerprint, now - _options.DuplicateDetectionWindow, cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation(
                "Duplicate notification request for customer {CustomerId}; returning existing notification {NotificationId}",
                customerId, existing.Id);
            return new CreateNotificationResult(NotificationResponse.From(existing), IsDuplicate: true);
        }

        var deduplicationKey = NotificationFingerprint.DeduplicationKey(fingerprint, now, _options.DuplicateDetectionWindow);
        var notification = Notification.Create(
            customerId,
            type,
            recipient,
            subject,
            message,
            priority,
            request.RequestedDeliveryTime ?? now, // omitted = deliver as soon as possible
            now,
            fingerprint,
            deduplicationKey);

        // 2) Database-level guard: the UNIQUE index on DeduplicationKey rejects a concurrent identical insert.
        if (await repository.TryAddAsync(notification, cancellationToken))
        {
            logger.LogInformation(
                "Accepted notification {NotificationId} for customer {CustomerId} with priority {Priority}, due {RequestedDeliveryTime}",
                notification.Id, customerId, priority, notification.RequestedDeliveryTime);
            return new CreateNotificationResult(NotificationResponse.From(notification), IsDuplicate: false);
        }

        var winner = await repository.GetByDeduplicationKeyAsync(deduplicationKey, cancellationToken)
                     ?? throw new InvalidOperationException(
                         $"Insert was rejected as a duplicate but no notification with key {deduplicationKey} was found.");

        logger.LogInformation(
            "Concurrent duplicate request for customer {CustomerId}; returning notification {NotificationId}",
            customerId, winner.Id);
        return new CreateNotificationResult(NotificationResponse.From(winner), IsDuplicate: true);
    }

    public async Task<NotificationResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await repository.GetByIdAsync(id, cancellationToken);
        return notification is null ? null : NotificationResponse.From(notification);
    }

    public async Task<PagedResponse<NotificationResponse>> GetForCustomerAsync(
        string customerId, string? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(customerId))
        {
            errors["customerId"] = ["Customer ID is required."];
        }

        if (page < 1)
        {
            errors["page"] = ["Page must be 1 or greater."];
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"Page size must be between 1 and {MaxPageSize}."];
        }

        NotificationStatus? statusFilter = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (EnumParser.TryParseName<NotificationStatus>(status, out var parsed))
            {
                statusFilter = parsed;
            }
            else
            {
                errors["status"] = ["Status must be one of: Pending, Processing, Sent, Failed."];
            }
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        var (items, total) = await repository.GetByCustomerAsync(customerId.Trim(), statusFilter, page, pageSize, cancellationToken);
        return new PagedResponse<NotificationResponse>(
            items.Select(NotificationResponse.From).ToList(), page, pageSize, total);
    }
}
