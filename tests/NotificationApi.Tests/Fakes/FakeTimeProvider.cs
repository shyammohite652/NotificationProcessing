namespace NotificationApi.Tests.Fakes;

// Fixed, controllable "now" so time rules are tested deterministically.
public class FakeTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
