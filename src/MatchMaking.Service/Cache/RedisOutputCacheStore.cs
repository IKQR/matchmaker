using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;

namespace MatchMaking.Service.Cache;

public class RedisOutputCacheStore(IDistributedCache cache) : IOutputCacheStore
{
    public async ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        await cache.RemoveAsync(tag, cancellationToken);
    }

    public async ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        return await cache.GetAsync(key, cancellationToken);
    }

    public async ValueTask SetAsync(string key, byte[] value, string[]? tags, TimeSpan validFor, CancellationToken cancellationToken)
    {
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = validFor
        };

        await cache.SetAsync(key, value, options, cancellationToken);

        if (tags != null)
        {
            foreach (var tag in tags)
            {
                await cache.SetAsync(tag, value, options, cancellationToken);
            }
        }
    }
}