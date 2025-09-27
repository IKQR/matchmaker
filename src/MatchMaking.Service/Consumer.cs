using MassTransit;
using MatchMaking.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace MatchMaking.Service;

public sealed class Consumer(ILogger<Consumer> logger, IDistributedCache distributedCache, IConfiguration configuration)
    : IConsumer<MatchmakingCompleteMessage>
{
    private readonly DistributedCacheEntryOptions _cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(configuration.GetValue<int>("Cache:MatchLifetime"))
    };

    public async Task Consume(ConsumeContext<MatchmakingCompleteMessage> context)
    {
        logger.LogInformation("Matchmaking completed for [{Users}] with ID: {MatchId}",
            string.Join(", ", context.Message.UserIds), context.Message.MatchId);
        
        var userCacheTasks = context.Message.UserIds
            .Select(userId => distributedCache.SetStringAsync("user:" + userId, context.Message.MatchId.ToString(), _cacheOptions))
            .ToList();
        
        var matchCacheTask = distributedCache.SetStringAsync("match:" + context.Message.MatchId,
            JsonSerializer.Serialize(context.Message), _cacheOptions);

        await Task.WhenAll(userCacheTasks.Concat([matchCacheTask]));
    }
}