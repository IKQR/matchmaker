using MassTransit;
using MatchMaking.Contracts;
using StackExchange.Redis;

namespace MatchMaking.Worker;

public sealed class Consumer(
    ILogger<Consumer> logger,
    IConnectionMultiplexer redis)
    : IConsumer<MatchmakingRequestMessage>
{
    private readonly IDatabase _db = redis.GetDatabase();
    
    public async Task Consume(ConsumeContext<MatchmakingRequestMessage> context)
    {
        try
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            await _db.SortedSetAddAsync("matchmaking:queue", context.Message.UserId.ToString(), timestamp);

            logger.LogInformation("Received matchmaking request for user {UserId} at {Timestamp}",
                context.Message.UserId, timestamp);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to process matchmaking request for user {UserId}", context.Message.UserId);
            
            throw;
        }
    }
}