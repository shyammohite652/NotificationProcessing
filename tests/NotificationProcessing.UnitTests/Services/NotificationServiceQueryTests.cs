using Microsoft.Extensions.Logging.Abstractions;
using NotificationProcessing.Application.Exceptions;
using NotificationProcessing.Application.Options;
using NotificationProcessing.Application.Services;
using NotificationProcessing.Application.Validators;
using NotificationProcessing.Domain.Enums;
using NotificationProcessing.UnitTests.Fakes;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace NotificationProcessing.UnitTests.Services;

public sealed class NotificationServiceQueryTests
{
    private readonly InMemoryNotificationRepository _repository = new();
    private readonly NotificationService _sut;

    public NotificationServiceQueryTests() => _sut = new NotificationService(
        _repository,
        new CreateNotificationRequestValidator(),
        new FakeTimeProvider(TestData.Now),
        MsOptions.Create(new NotificationOptions()),
        NullLogger<NotificationService>.Instance);

    // 14. Unknown id → null, which the controller turns into 404.
    [Fact]
    public async Task Unknown_id_returns_null()
    {
        Assert.Null(await _sut.GetByIdAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Existing_id_returns_the_notification()
    {
        var stored = TestData.StoredNotification(NotificationPriority.High, TestData.Now);
        _repository.Seed(stored);

        var result = await _sut.GetByIdAsync(stored.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(stored.Id, result.Id);
    }

    [Fact]
    public async Task Customer_notifications_are_paged_newest_first()
    {
        var oldest = TestData.StoredNotification(NotificationPriority.Low, TestData.Now);
        var middle = TestData.StoredNotification(NotificationPriority.Low, TestData.Now.AddMinutes(1));
        var newest = TestData.StoredNotification(NotificationPriority.Low, TestData.Now.AddMinutes(2));
        _repository.Seed(oldest, middle, newest);

        var page1 = await _sut.GetForCustomerAsync("CUST-101", null, page: 1, pageSize: 2, CancellationToken.None);
        var page2 = await _sut.GetForCustomerAsync("CUST-101", null, page: 2, pageSize: 2, CancellationToken.None);

        Assert.Equal(new[] { newest.Id, middle.Id }, page1.Items.Select(i => i.Id).ToArray());
        Assert.Equal(new[] { oldest.Id }, page2.Items.Select(i => i.Id).ToArray());
        Assert.Equal(3, page1.TotalCount);
        Assert.Equal(2, page1.TotalPages);
    }

    [Fact]
    public async Task Unknown_customer_returns_an_empty_page()
    {
        var result = await _sut.GetForCustomerAsync("NOBODY", null, 1, 20, CancellationToken.None);

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public async Task Invalid_paging_is_rejected(int page, int pageSize, string field)
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _sut.GetForCustomerAsync("CUST-101", null, page, pageSize, CancellationToken.None));
        Assert.Contains(field, ex.Errors.Keys);
    }

    [Fact]
    public async Task Invalid_status_filter_is_rejected()
    {
        var ex = await Assert.ThrowsAsync<RequestValidationException>(
            () => _sut.GetForCustomerAsync("CUST-101", "Delivered", 1, 20, CancellationToken.None));
        Assert.Contains("status", ex.Errors.Keys);
    }
}
