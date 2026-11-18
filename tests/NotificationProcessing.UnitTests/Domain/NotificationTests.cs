using NotificationProcessing.Domain.Enums;
using NotificationProcessing.Domain.Exceptions;
using NotificationProcessing.UnitTests.Fakes;

namespace NotificationProcessing.UnitTests.Domain;

/// <summary>The status state machine: only Pending → Processing → Sent/Failed is allowed.</summary>
public sealed class NotificationTests
{
    [Fact]
    public void New_notification_is_pending()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);

        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.NotEqual(Guid.Empty, n.Id);
        Assert.Equal(0, n.AttemptCount);
    }

    [Fact]
    public void Cannot_mark_sent_without_being_processed_first()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);

        Assert.Throws<InvalidNotificationStateException>(() => n.MarkSent(TestData.Now));
    }

    [Fact]
    public void Cannot_claim_a_notification_twice()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        n.MarkProcessing(TestData.Now);

        Assert.Throws<InvalidNotificationStateException>(() => n.MarkProcessing(TestData.Now));
    }

    [Fact]
    public void Failure_reason_is_truncated_to_column_length()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now);
        n.MarkProcessing(TestData.Now);

        n.MarkFailed(new string('x', 5000), TestData.Now);

        Assert.Equal(1000, n.FailureReason!.Length);
    }

    [Fact]
    public void Is_due_exactly_at_requested_delivery_time()
    {
        var n = TestData.StoredNotification(NotificationPriority.Normal, TestData.Now, deliverAt: TestData.Now.AddMinutes(5));

        Assert.False(n.IsDueAt(TestData.Now.AddMinutes(5).AddTicks(-1)));
        Assert.True(n.IsDueAt(TestData.Now.AddMinutes(5)));
    }
}
