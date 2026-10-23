using System;
using System.Diagnostics;

namespace AutoShutdown.Core;

/// <summary>
/// A monotonic countdown. Uses a Stopwatch rather than DateTime so clock changes
/// and time-zone jumps cannot shorten or extend the timer.
/// </summary>
public sealed class CountdownEngine
{
    private readonly Stopwatch _clock = new();
    private TimeSpan _total;

    public TimeSpan Total => _total;

    public bool IsRunning { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            var remaining = _total - _clock.Elapsed;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    public double Progress => _total.TotalSeconds <= 0
        ? 0
        : Math.Clamp(Remaining.TotalSeconds / _total.TotalSeconds, 0, 1);

    public void Start(TimeSpan total)
    {
        _total = total;
        _clock.Restart();
        IsRunning = true;
    }

    public void Stop()
    {
        _clock.Stop();
        IsRunning = false;
    }

    /// <summary>
    /// Extends a running countdown ("+5 minutes on the go"). The Stopwatch
    /// keeps its elapsed base, so the pie simply grows back.
    /// </summary>
    public void Add(TimeSpan amount)
    {
        if (!IsRunning || amount <= TimeSpan.Zero)
        {
            return;
        }

        _total += amount;
    }
}
