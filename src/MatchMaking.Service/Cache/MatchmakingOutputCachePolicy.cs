using Microsoft.AspNetCore.OutputCaching;

namespace MatchMaking.Service.Cache;

public class MatchmakingOutputCachePolicy : IOutputCachePolicy
{
    public const string PolicyName  = "Matchmaking";
    
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        var attemptOutputCaching = context.HttpContext.Response.StatusCode == StatusCodes.Status200OK;
        context.EnableOutputCaching = attemptOutputCaching;
        context.AllowCacheLookup = attemptOutputCaching;
        
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
    {
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
        {
            context.AllowCacheStorage = false;
        }

        return ValueTask.CompletedTask;
    }
}