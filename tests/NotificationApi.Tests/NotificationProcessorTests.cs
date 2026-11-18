using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NotificationApi.Models;
using NotificationApi.Providers;
using NotificationApi.Services;
using NotificationApi.Tests.Fakes;

namespace NotificationApi.Tests;

public class NotificationProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Now);
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly Mock<INotificationProvider> _provider = new();
    private readonly List<Guid> _sentInOrder = [];

    public NotificationProcessorTests()
    {
        _provider.Setup(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Callback<Notification, CancellationToken>((n, _) => _sentInOrder.Add(n.Id))
            .Returns(Task.CompletedTask);
    }

    private NotificationProcessor CreateProcessor(INotificationProvider? provider = null) =>
        new(_repository, provider ?? _provider.Object, _time, NullLogger<NotificationProcessor>.Instance);

    private Notification Add(NotificationPriority priority, DateTimeOffset createdAt, DateTimeOffset? deliverAt = null)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(),
            CustomerId = "CUST-101",
            Type = NotificationType.Email,
            Recipient = "customer@example.com",
            Message = "Hello",
            Priority = priority,
            CreatedAt = createdAt,
            RequestedDeliveryTime = deliverAt ?? createdAt,
            Status = NotificationStatus.Pending,
            DeduplicationKey = Guid.NewGuid().ToString()
        };
        _repository.Items.Add(n);
        return n;
    }

    [Fact] // 9 – High was created last but is sent first
    public async Task High_priority_is_processed_first()
    {
        var low = Add(NotificationPriority.Low, Now.AddMinutes(-3));
        var normal = Add(NotificationPriority.Normal, Now.AddMinutes(-2));
        var high = Add(NotificationPriority.High, Now.AddMinutes(-1));

        await CreateProcessor().ProcessPendingAsync(default);

        Assert.Equal(new[] { high.Id, normal.Id, low.Id }, _sentInOrder);
    }

    [Fact] // 10 – added out of order on purpose
    public async Task Same_priority_is_processed_oldest_first()
    {
        var second = Add(NotificationPriority.Normal, Now.AddMinutes(-2));
        var first = Add(NotificationPriority.Normal, Now.AddMinutes(-3));
        var third = Add(NotificationPriority.Normal, Now.AddMinutes(-1));

        await CreateProcessor().ProcessPendingAsync(default);

        Assert.Equal(new[] { first.Id, second.Id, third.Id }, _sentInOrder);
    }

    [Fact] // 11
    public async Task Future_notification_stays_pending_until_due()
    {
        var future = Add(NotificationPriority.High, Now, deliverAt: Now.AddHours(1));

        await CreateProcessor().ProcessPendingAsync(default);
        Assert.Equal(NotificationStatus.Pending, future.Status);
        Assert.Empty(_sentInOrder);

        _time.Now = Now.AddHours(1); // now due
        await CreateProcessor().ProcessPendingAsync(default);
        Assert.Equal(NotificationStatus.Sent, future.Status);
    }

    [Fact] // 12
    public async Task Successful_send_sets_status_sent()
    {
        var n = Add(NotificationPriority.Normal, Now);

        var result = await CreateProcessor().ProcessPendingAsync(default);

        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(Now, n.ProcessedAt);
        Assert.Equal(1, result.Sent);
    }

    [Fact] // 13 – one failure does not stop the others
    public async Task Provider_failure_sets_status_failed_and_others_still_send()
    {
        var broken = Add(NotificationPriority.High, Now.AddMinutes(-1));
        var healthy = Add(NotificationPriority.Normal, Now);
        _provider.Setup(p => p.SendAsync(It.Is<Notification>(n => n.Id == broken.Id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP server unreachable"));

        var result = await CreateProcessor().ProcessPendingAsync(default);

        Assert.Equal(NotificationStatus.Failed, broken.Status);
        Assert.Equal("SMTP server unreachable", broken.FailureReason);
        Assert.Equal(NotificationStatus.Sent, healthy.Status);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Sent);
    }

    [Fact]
    public async Task Already_sent_notifications_are_not_sent_again()
    {
        Add(NotificationPriority.Normal, Now);
        var processor = CreateProcessor();

        await processor.ProcessPendingAsync(default);
        await processor.ProcessPendingAsync(default);

        _provider.Verify(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact] // 15 – parallel runs see the same list, but each notification is sent exactly once
    public async Task Concurrent_processing_sends_each_notification_once()
    {
        for (var i = 0; i < 30; i++) Add((NotificationPriority)(i % 3), Now.AddSeconds(-i));

        var sendCount = new ConcurrentDictionary<Guid, int>();
        var provider = new Mock<INotificationProvider>();
        provider.Setup(p => p.SendAsync(It.IsAny<Notification>(), It.IsAny<CancellationToken>()))
            .Returns(async (Notification n, CancellationToken ct) =>
            {
                sendCount.AddOrUpdate(n.Id, 1, (_, c) => c + 1);
                await Task.Delay(1, ct);
            });

        var runs = Enumerable.Range(0, 4)
            .Select(_ => Task.Run(() => CreateProcessor(provider.Object).ProcessPendingAsync(default)));
        var results = await Task.WhenAll(runs);

        Assert.Equal(30, sendCount.Count);
        Assert.All(sendCount.Values, c => Assert.Equal(1, c));
        Assert.Equal(30, results.Sum(r => r.Sent));
    }
}
