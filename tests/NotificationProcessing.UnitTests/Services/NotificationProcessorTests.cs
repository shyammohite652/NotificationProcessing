using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationProcessing.Application.Interfaces;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Domain.Entities;
using NotificationProcessing.Domain.Enums;
using NotificationProcessing.UnitTests.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationProcessing.UnitTests.Services;

/// <summary>
/// Processing rules: what is eligible, in which order, what happens on success/failure,
/// and that concurrent runs never send the same notification twice.
/// </summary>
public sealed class NotificationProcessorTests
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly Mock<INotificationProvider> _provider = new();
    private readonly List<Guid> _sendOrder = [];

    public NotificationProcessorTests()
    {
        _provider.SetupGet(p => p.Channel).Returns(NotificationType.Email);
        _provider.Setup(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => _sendOrder.Add(n.Id))
            .ReturnsAsync(NotificationDeliveryResult.Success());
    }

    private NotificationProcessor CreateProcessor(int batchSize = 50, INotificationProvider? provider = null) => new(
        _repository,
        new[] { provider ?? _provider.Object },
        _time,
        MsOptions.Create(new NotificationOptions { ProcessingBatchSize = batchSize }),
        NullLogger<NotificationProcessor>.Instance);

    private Notification Status(Notification n) => _repository.All.Single(x => x.Id == n.Id);

    // 9. Priority beats creation order: Low and Normal were created first, High is still sent first.
    [Fact]
    public async Task High_priority_is_processed_before_normal_and_low()
    {
        var low = TestData.StoredNotification(NotificationPriority.Low, TestData.Now.AddMinutes(-3));
        var normal = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now.AddMinutes(-2));
        var high = TestData.StoredNotification(NotificationPriority.High, TestData.Now.AddMinutes(-1));
        _repository.Seed(low, normal, high);

        await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { high.Id, normal.Id, low.Id }, _sendOrder.ToArray());
    }

    // 10. Within the same priority: first in, first out by CreatedAt (seeded out of order on purpose).
    [Fact]
    public async Task Equal_priority_is_processed_fifo_by_creation_time()
    {
        var second = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now.AddMinutes(-2));
        var third = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now.AddMinutes(-1));
        var first = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now.AddMinutes(-3));
        _repository.Seed(second, third, first);

        await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, _sendOrder.ToArray());
    }

    [Fact]
    public async Task Batch_size_limits_work_and_takes_highest_priority_first()
    {
        var low = TestData.StoredNotification(NotificationPriority.Low, TestData.Now.AddMinutes(-5));
        var high = TestData.StoredNotification(NotificationPriority.High, TestData.Now);
        _repository.Seed(low, high);

        var result = await CreateProcessor(batchSize: 1).ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, result.Selected);
        Assert.Equal(NotificationStatus.Sent, Status(high).Status);
        Assert.Equal(NotificationStatus.Pending, Status(low).Status);
    }

    // 11. Scheduled delivery: future notifications stay Pending and are picked up once due.
    [Fact]
    public async Task Future_notifications_are_not_processed_until_due()
    {
        var future = TestData.StoredNotification(NotificationPriority.High, TestData.Now, deliverAt: TestData.Now.AddMinutes(30));
        _repository.Seed(future);

        var firstRun = await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, firstRun.Selected);
        Assert.Equal(NotificationStatus.Pending, Status(future).Status);
        _provider.Verify(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never());

        _time.Advance(TimeSpan.FromMinutes(30)); // now == RequestedDeliveryTime → due
        var secondRun = await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, secondRun.Sent);
        Assert.Equal(NotificationStatus.Sent, Status(future).Status);
    }

    // 12. Success path.
    [Fact]
    public async Task Successful_send_marks_notification_sent()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        _repository.Seed(n);

        var result = await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        var stored = Status(n);
        Assert.Equal(NotificationStatus.Sent, stored.Status);
        Assert.Equal(TestData.Now, stored.ProcessedAt);
        Assert.Null(stored.FailureReason);
        Assert.Equal(1, stored.AttemptCount);
        Assert.Equal(1, result.Sent);
    }

    [Fact]
    public async Task Sent_notifications_are_not_processed_again()
    {
        _repository.Seed(TestData.StoredNotification(NotificationPriority.Normal, TestData.Now));
        var processor = CreateProcessor();

        await processor.ProcessPendingAsync(CancellationToken.None);
        var secondRun = await processor.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, secondRun.Selected);
        _provider.Verify(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    // 13a. Provider reports failure → Failed with the reason recorded.
    [Fact]
    public async Task Provider_failure_result_marks_notification_failed_with_reason()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        _repository.Seed(n);
        _provider.Setup(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.Failure("Mailbox unavailable"));

        var result = await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(NotificationStatus.Failed, Status(n).Status);
        Assert.Equal("Mailbox unavailable", Status(n).FailureReason);
        Assert.Equal(1, result.Failed);
    }

    // 13b. Provider throws → caught, Failed, and the rest of the batch continues.
    [Fact]
    public async Task Provider_exception_marks_that_notification_failed_and_others_continue()
    {
        var broken = TestData.StoredNotification(NotificationPriority.High, TestData.Now.AddMinutes(-2));
        var healthy = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now.AddMinutes(-1));
        _repository.Seed(broken, healthy);
        _provider.Setup(p => p.SendAsync(It.Is<Notification>(n => n.Id == broken.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("SMTP server unreachable"));

        var result = await CreateProcessor().ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(NotificationStatus.Failed, Status(broken).Status);
        Assert.Contains("SMTP server unreachable", Status(broken).FailureReason);
        Assert.Equal(NotificationStatus.Sent, Status(healthy).Status);
        Assert.Equal(1, result.Sent);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public async Task Missing_provider_for_channel_marks_failed_without_throwing()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        _repository.Seed(n);
        var otherChannel = new Mock<INotificationProvider>();
        otherChannel.SetupGet(p => p.Channel).Returns((NotificationType)99);

        await CreateProcessor(provider: otherChannel.Object).ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(NotificationStatus.Failed, Status(n).Status);
    }

    [Fact]
    public async Task Notification_claimed_by_someone_else_is_skipped()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        var repository = new Mock<INotificationRepository>();
        repository.Setup(r => r.GetDueForProcessingAsync(It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Notification> { n });
        repository.Setup(r => r.TryMarkProcessingAsync(n.Id, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var processor = new NotificationProcessor(
            repository.Object, new[] { _provider.Object }, _time,
            MsOptions.Create(new NotificationOptions()), NullLogger<NotificationProcessor>.Instance);

        var result = await processor.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        _provider.Verify(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Never());
        repository.Verify(r => r.MarkSentAsync(It.IsAny<Guid>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    // 15. Two processing runs at the same time read the same candidates; the atomic claim lets only one send each.
    [Fact]
    public async Task Concurrent_runs_never_send_the_same_notification_twice()
    {
        const int count = 40;
        var notifications = Enumerable.Range(0, count)
            .Select(i => TestData.StoredNotification((NotificationPriority)(i % 3), TestData.Now.AddSeconds(-i)))
            .ToArray();
        _repository.Seed(notifications);

        var sendsPerId = new ConcurrentDictionary<Guid, int>();
        var provider = new Mock<INotificationProvider>();
        provider.SetupGet(p => p.Channel).Returns(NotificationType.Email);
        provider.Setup(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Returns(async (Notification n, CancellationToken ct) =>
            {
                sendsPerId.AddOrUpdate(n.Id, 1, (_, sends) => sends + 1);
                await Task.Delay(1, ct);
                return NotificationDeliveryResult.Success();
            });

        var runs = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => CreateProcessor(provider: provider.Object).ProcessPendingAsync(CancellationToken.None)))
            .ToArray();
        var results = await Task.WhenAll(runs);

        Assert.Equal(count, sendsPerId.Count);
        Assert.All(sendsPerId.Values, sends => Assert.Equal(1, sends));
        Assert.Equal(count, results.Sum(r => r.Sent));
        Assert.All(_repository.All, n => Assert.Equal(NotificationStatus.Sent, n.Status));
    }
}
