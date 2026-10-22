namespace Casefile.Core.Seed;

/// <summary>A controllable clock so seeded data and tests are reproducible.</summary>
public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
