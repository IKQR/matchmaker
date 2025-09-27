namespace MatchMaking.Worker.Options;

public sealed class AssemblerOptions
{
    public int BatchSize { get; init; } = 3;
    public string QueueKey { get; init; } = "matchmaking:queue";
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(300);
}