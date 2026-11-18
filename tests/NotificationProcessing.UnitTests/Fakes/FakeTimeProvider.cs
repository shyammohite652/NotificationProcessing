namespace NotificationProcessing.UnitTests.Fakes;

/// <summary>A controllable clock so time-based rules (past / now / future) are tested deterministically.</summary>
public sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
