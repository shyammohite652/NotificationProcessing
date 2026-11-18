using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationProcessing.Application.DTOs;
using NotificationProcessing.Application.Exceptions;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Application.Validators;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;
using NotificationProcessing.UnitTests.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationProcessing.UnitTests.Services;

/// <summary>
/// Creation rules: validation, defaults and duplicate prevention.
/// Each validation test also checks that nothing was stored, because "rejected" must mean "not persisted".
/// </summary>
public sealed class NotificationServiceCreateTests
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly NotificationService _sut;

    public NotificationServiceCreateTests() => _sut = CreateService(_repository);

    private NotificationService CreateService(INotificationRepository repository) => new(
        repository,
        new CreateNotificationRequestValidator(),
        _time,
        MsOptions.Create(new NotificationOptions { DuplicateDetectionWindow = Window }),
        NullLogger<NotificationService>.Instance);

    private CreateNotificationRequest Valid() => TestData.ValidRequest(_time.GetUtcNow());

    private async Task AssertRejectedAsync(CreateNotificationRequest request, string field)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => _sut.CreateAsync(request, CancellationToken.None));
        Assert.Contains(field, ex.Errors.Keys);
        Assert.Empty(_repository.All);
    }

    // 1. Happy path: stored as Pending with server-side timestamps.
    [Fact]
    public async Task Valid_request_is_stored_as_pending()
    {
        var request = Valid();

        var result = await _sut.CreateAsync(request, CancellationToken.None);

        Assert.False(result.IsDuplicate);
        var stored = Assert.Single(_repository.All);
        Assert.Equal(stored.Id, result.Notification.Id);
        Assert.Equal(NotificationStatus.Pending, stored.Status);
        Assert.Equal(TestData.Now, stored.CreatedAt);
        Assert.Equal(request.RequestedDeliveryTime, stored.RequestedDeliveryTime);
        Assert.Equal(NotificationPriority.Normal, stored.Priority);
        Assert.Equal(NotificationType.Email, stored.Type);
    }

    [Fact]
    public async Task Priority_and_type_are_case_insensitive_and_values_are_trimmed()
    {
        var result = await _sut.CreateAsync(
            Valid() with { Priority = "high", Type = "email", CustomerId = "  CUST-101  ", Recipient = " customer@example.com " },
            CancellationToken.None);

        Assert.Equal(NotificationPriority.High, result.Notification.Priority);
        Assert.Equal("CUST-101", result.Notification.CustomerId);
        Assert.Equal("customer@example.com", result.Notification.Recipient);
    }

    [Fact]
    public async Task Omitted_priority_defaults_to_normal_and_omitted_delivery_time_means_now()
    {
        var result = await _sut.CreateAsync(Valid() with { Priority = null, RequestedDeliveryTime = null }, CancellationToken.None);

        Assert.Equal(NotificationPriority.Normal, result.Notification.Priority);
        Assert.Equal(TestData.Now, result.Notification.RequestedDeliveryTime);
    }

    // 2. Missing CustomerId (null, empty and whitespace are all "missing").
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public Task Missing_customer_id_is_rejected(string? customerId) =>
        AssertRejectedAsync(Valid() with { CustomerId = customerId }, "customerId");

    // 3. Missing Recipient.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public Task Missing_recipient_is_rejected(string? recipient) =>
        AssertRejectedAsync(Valid() with { Recipient = recipient }, "recipient");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public Task Missing_message_is_rejected(string? message) =>
        AssertRejectedAsync(Valid() with { Message = message }, "message");

    [Theory]
    [InlineData(null)]
    [InlineData("Sms")]   // not supported yet
    [InlineData("1")]     // numeric values must not be accepted as enum names
    public Task Missing_or_unsupported_type_is_rejected(string? type) =>
        AssertRejectedAsync(Valid() with { Type = type }, "type");

    // 4. Invalid email address, including forms MailAddress would otherwise accept.
    [Theory]
    [InlineData("not-an-email")]
    [InlineData("customer@")]
    [InlineData("@example.com")]
    [InlineData("customer@localhost")]
    [InlineData("John <john@example.com>")]
    [InlineData("two words@example.com")]
    public Task Invalid_email_is_rejected(string recipient) =>
        AssertRejectedAsync(Valid() with { Recipient = recipient }, "recipient");

    // 5. Invalid priority. "5" matters: Enum.TryParse alone would accept it.
    [Theory]
    [InlineData("Urgent")]
    [InlineData("5")]
    [InlineData("")]
    public Task Invalid_priority_is_rejected(string priority) =>
        AssertRejectedAsync(Valid() with { Priority = priority }, "priority");

    // 6. Past delivery time.
    [Fact]
    public Task Delivery_time_in_the_past_is_rejected() =>
        AssertRejectedAsync(Valid() with { RequestedDeliveryTime = TestData.Now.AddTicks(-1) }, "requestedDeliveryTime");

    // 7. Exactly "now" is not in the past, so it is accepted.
    [Fact]
    public async Task Delivery_time_equal_to_now_is_accepted()
    {
        var result = await _sut.CreateAsync(Valid() with { RequestedDeliveryTime = TestData.Now }, CancellationToken.None);

        Assert.Equal(TestData.Now, result.Notification.RequestedDeliveryTime);
        Assert.Single(_repository.All);
    }

    [Fact]
    public async Task All_validation_errors_are_reported_together()
    {
        var request = new CreateNotificationRequest
        {
            CustomerId = "",
            Type = "Email",
            Recipient = "bad",
            Message = "",
            Priority = "Urgent",
            RequestedDeliveryTime = TestData.Now.AddDays(-1)
        };

        var ex = await Assert.ThrowsAsync<RequestValidationException>(() => _sut.CreateAsync(request, CancellationToken.None));

        Assert.Equal(
            new[] { "customerId", "message", "priority", "recipient", "requestedDeliveryTime" },
            ex.Errors.Keys.OrderBy(k => k).ToArray());
    }

    // 8. Duplicate request (client retry): same notification returned, nothing new stored.
    [Fact]
    public async Task Identical_request_within_window_returns_the_original()
    {
        var first = await _sut.CreateAsync(Valid(), CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(30));

        var second = await _sut.CreateAsync(Valid() with { RequestedDeliveryTime = first.Notification.RequestedDeliveryTime }, CancellationToken.None);

        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Notification.Id, second.Notification.Id);
        Assert.Single(_repository.All);
    }

    [Fact]
    public async Task Duplicate_detection_ignores_recipient_case()
    {
        var request = Valid();
        var first = await _sut.CreateAsync(request, CancellationToken.None);

        var second = await _sut.CreateAsync(request with { Recipient = "Customer@Example.COM" }, CancellationToken.None);

        Assert.True(second.IsDuplicate);
        Assert.Equal(first.Notification.Id, second.Notification.Id);
    }

    [Fact]
    public async Task Different_content_is_not_a_duplicate()
    {
        var request = Valid();
        await _sut.CreateAsync(request, CancellationToken.None);

        var second = await _sut.CreateAsync(request with { Message = "Your order has shipped." }, CancellationToken.None);

        Assert.False(second.IsDuplicate);
        Assert.Equal(2, _repository.All.Count);
    }

    [Fact]
    public async Task Identical_request_after_window_creates_a_new_notification()
    {
        var request = Valid() with { RequestedDeliveryTime = null };
        var first = await _sut.CreateAsync(request, CancellationToken.None);
        _time.Advance(Window + TimeSpan.FromSeconds(1));

        var second = await _sut.CreateAsync(request, CancellationToken.None);

        Assert.False(second.IsDuplicate);
        Assert.NotEqual(first.Notification.Id, second.Notification.Id);
    }

    /// <summary>
    /// Race: both requests pass the "already exists?" check, then the unique index rejects the second insert.
    /// The service must return the winner instead of failing with a 500.
    /// </summary>
    [Fact]
    public async Task Concurrent_duplicate_rejected_by_unique_index_returns_the_winner()
    {
        var winner = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        var repository = new Mock<INotificationRepository>();
        repository.Setup(r => r.FindRecentByFingerprintAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null);
        repository.Setup(r => r.TryAddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repository.Setup(r => r.GetByDeduplicationKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(winner);

        var result = await CreateService(repository.Object).CreateAsync(Valid(), CancellationToken.None);

        Assert.True(result.IsDuplicate);
        Assert.Equal(winner.Id, result.Notification.Id);
        repository.Verify(r => r.TryAddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Once());
    }
}
