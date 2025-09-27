namespace MatchMaking.Worker.Options;

public sealed class OutboxerOptions
{
    public string OutboxListKey { get; init; } = "matchmaking:outbox";
    public string DeadLetterListKey { get; init; } = "matchmaking:dead";
    public TimeSpan IdleDelay { get; init; } = TimeSpan.FromMilliseconds(300);
    public TimeSpan PopTimeout { get; init; } = TimeSpan.FromSeconds(1);
}