using AutoShutdown.Models;

namespace AutoShutdown.Core;

/// <summary>Last-used values, remembered between launches.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public PowerActionOption Action { get; set; } = PowerActionOption.Shutdown;

    public int Hours { get; set; }

    public int Minutes { get; set; } = 30;

    public int Seconds { get; set; }
}
