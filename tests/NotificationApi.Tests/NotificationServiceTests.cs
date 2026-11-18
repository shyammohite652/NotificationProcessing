using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationApi.Data;
using NotificationApi.Models;
using NotificationApi.Services;
using NotificationApi.Tests.Fakes;

namespace NotificationApi.Tests;

public class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly NotificationService _service;

    public NotificationServiceTests() =>
        _service = new NotificationService(_repository, _time, NullLogger<NotificationService>.Instance);

    private static CreateNotificationRequest ValidRequest() => new()
    {
        CustomerId = "CUST-101",
        Type = "Email",
        Recipient = "customer@example.com",
        Subject = "Order Confirmed",
        Message = "Your order has been confirmed.",
        Priority = "Normal",
        RequestedDeliveryTime = Now.AddHours(1)
    };

    // Asserts the request is rejected for the given field AND nothing was saved.
    private async Task AssertInvalid(CreateNotificationRequest request, string field)
    {
        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request, default));
        Assert.Contains(field, ex.Errors.Keys);
        Assert.Empty(_repository.Items);
    }

    [Fact] // 1
    public async Task Valid_request_is_saved_as_pending()
    {
        var (result, isDuplicate) = await _service.CreateAsync(ValidRequest(), default);

        Assert.False(isDuplicate);
        Assert.Equal(NotificationStatus.Pending, result.Status);
        Assert.Equal(Now, result.CreatedAt);
        Assert.Equal(NotificationPriority.Normal, result.Priority);
        Assert.Single(_repository.Items);
    }

    [Fact]
    public async Task Priority_is_optional_and_defaults_to_normal()
    {
        var (result, _) = await _service.CreateAsync(ValidRequest() with { Priority = null }, default);

        Assert.Equal(NotificationPriority.Normal, result.Priority);
    }

    [Theory] // 2 – whitespace counts as missing
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public Task Missing_customer_id_is_rejected(string? customerId) =>
        AssertInvalid(ValidRequest() with { CustomerId = customerId }, "customerId");

    [Theory] // 3
    [InlineData(null)]
    [InlineData("")]
    public Task Missing_recipient_is_rejected(string? recipient) =>
        AssertInvalid(ValidRequest() with { Recipient = recipient }, "recipient");

    [Fact]
    public Task Missing_message_is_rejected() =>
        AssertInvalid(ValidRequest() with { Message = "" }, "message");

    [Theory]
    [InlineData(null)]
    [InlineData("Sms")]
    public Task Missing_or_unsupported_type_is_rejected(string? type) =>
        AssertInvalid(ValidRequest() with { Type = type }, "type");

    [Theory] // 4 – includes forms that MailAddress would accept on its own
    [InlineData("not-an-email")]
    [InlineData("customer@")]
    [InlineData("customer@localhost")]
    [InlineData("John <john@example.com>")]
    public Task Invalid_email_is_rejected(string recipient) =>
        AssertInvalid(ValidRequest() with { Recipient = recipient }, "recipient");

    [Theory] // 5 – "2" matters: Enum.TryParse alone would accept it
    [InlineData("Urgent")]
    [InlineData("2")]
    public Task Invalid_priority_is_rejected(string priority) =>
        AssertInvalid(ValidRequest() with { Priority = priority }, "priority");

    [Fact] // 6
    public Task Delivery_time_in_the_past_is_rejected() =>
        AssertInvalid(ValidRequest() with { RequestedDeliveryTime = Now.AddSeconds(-1) }, "requestedDeliveryTime");

    [Fact] // 7 – "now" is not in the past
    public async Task Delivery_time_equal_to_now_is_accepted()
    {
        var (result, _) = await _service.CreateAsync(ValidRequest() with { RequestedDeliveryTime = Now }, default);

        Assert.Equal(Now, result.RequestedDeliveryTime);
    }

    [Fact] // 8 – client retry
    public async Task Same_request_twice_returns_the_original_and_saves_once()
    {
        var (first, _) = await _service.CreateAsync(ValidRequest(), default);
        _time.Now = Now.AddMinutes(5);

        var (second, isDuplicate) = await _service.CreateAsync(ValidRequest(), default);

        Assert.True(isDuplicate);
        Assert.Equal(first.Id, second.Id);
        Assert.Single(_repository.Items);
    }

    [Fact] // 8 – the retry lands in the next 10-minute slot (10:09 → 10:11) and is still caught
    public async Task Retry_across_a_slot_boundary_is_still_a_duplicate()
    {
        _time.Now = Now.AddMinutes(9);
        var (first, _) = await _service.CreateAsync(ValidRequest(), default);
        _time.Now = Now.AddMinutes(11);

        var (second, isDuplicate) = await _service.CreateAsync(ValidRequest(), default);

        Assert.True(isDuplicate);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Different_message_is_not_a_duplicate()
    {
        await _service.CreateAsync(ValidRequest(), default);

        var (_, isDuplicate) = await _service.CreateAsync(ValidRequest() with { Message = "Your order has shipped." }, default);

        Assert.False(isDuplicate);
        Assert.Equal(2, _repository.Items.Count);
    }

    [Fact]
    public async Task Same_request_long_after_the_window_is_a_new_notification()
    {
        await _service.CreateAsync(ValidRequest(), default);
        _time.Now = Now.AddMinutes(21); // past the current and previous 10-minute slots

        var (_, isDuplicate) = await _service.CreateAsync(ValidRequest(), default);

        Assert.False(isDuplicate);
    }

    [Fact] // 8 – two identical requests at the same moment: the unique index rejects the second insert
    public async Task Concurrent_duplicate_returns_the_saved_notification()
    {
        var saved = new Notification { Id = Guid.NewGuid(), CustomerId = "CUST-101", Recipient = "customer@example.com", Message = "x" };
        var repository = new Mock<INotificationRepository>();
        repository.SetupSequence(r => r.FindByDeduplicationKeyAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Notification?)null)  // pre-check: not there yet
            .ReturnsAsync(saved);               // after the failed insert: the winner
        repository.Setup(r => r.TryAddAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var service = new NotificationService(repository.Object, _time, NullLogger<NotificationService>.Instance);

        var (result, isDuplicate) = await service.CreateAsync(ValidRequest(), default);

        Assert.True(isDuplicate);
        Assert.Equal(saved.Id, result.Id);
    }

    [Fact] // 14
    public async Task Unknown_id_returns_null()
    {
        Assert.Null(await _service.GetByIdAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Customer_notifications_are_paged_newest_first()
    {
        await _service.CreateAsync(ValidRequest() with { Message = "first" }, default);
        _time.Now = Now.AddMinutes(1);
        await _service.CreateAsync(ValidRequest() with { Message = "second" }, default);
        await _service.CreateAsync(ValidRequest() with { CustomerId = "OTHER" }, default);

        var page = await _service.GetForCustomerAsync("CUST-101", page: 1, pageSize: 1, default);

        Assert.Equal("second", Assert.Single(page.Items).Message);
        Assert.Equal(2, page.TotalCount);
    }
}
