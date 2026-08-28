namespace LX.Core.Time;

public readonly record struct ClockFrame(
    ulong FrameIndex,
    double UnscaledDeltaSeconds,
    double DeltaSeconds,
    double UnscaledElapsedSeconds,
    double ElapsedSeconds);

public sealed class GameClock
{
    private double _timeScale = 1.0;

    public bool IsPaused { get; set; }

    public double TimeScale
    {
        get => _timeScale;
        set
        {
            if (!double.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Time scale must be finite and non-negative.");
            }

            _timeScale = value;
        }
    }

    public ulong FrameIndex { get; private set; }

    public double ElapsedSeconds { get; private set; }

    public double UnscaledElapsedSeconds { get; private set; }

    public ClockFrame Advance(double unscaledDeltaSeconds)
    {
        if (!double.IsFinite(unscaledDeltaSeconds) || unscaledDeltaSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unscaledDeltaSeconds));
        }

        FrameIndex++;
        UnscaledElapsedSeconds += unscaledDeltaSeconds;
        var scaledDelta = IsPaused ? 0 : unscaledDeltaSeconds * TimeScale;
        ElapsedSeconds += scaledDelta;

        return new ClockFrame(
            FrameIndex,
            unscaledDeltaSeconds,
            scaledDelta,
            UnscaledElapsedSeconds,
            ElapsedSeconds);
    }
}
