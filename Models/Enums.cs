namespace AutoShutdown.Models;

/// <summary>What happens when the countdown reaches zero.</summary>
public enum PowerActionOption
{
    Nothing,
    Shutdown,
    Restart,
    Sleep,
    Hibernate,
    Lock
}
