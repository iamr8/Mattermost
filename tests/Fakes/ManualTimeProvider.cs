namespace MattermostMcp.Tests.Fakes;

/// <summary>A clock that moves only when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
