using MatchMaking.Worker.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MatchMaking.Worker.Workers;

public sealed class Assembler(
    ILogger<Assembler> logger,
    IConnectionMultiplexer redis,
    IOptions<AssemblerOptions> options)
    : BackgroundService
{
    private readonly IDatabase _db = redis.GetDatabase();
    private readonly AssemblerOptions _options = options.Value;

    private const string PopFullBatchLua =
        """
        local key      = KEYS[1]
        local outbox   = KEYS[2]
        local need     = tonumber(ARGV[1])
        local matchId  = ARGV[2]
        
        if redis.call('ZCARD', key) < need then
          return {}
        end
        
        local res = redis.call('ZPOPMIN', key, need)
        local members = {}
        for i = 1, #res, 2 do
          table.insert(members, res[i])
        end
        
        local now = tonumber(redis.call('TIME')[1])
        
        local payload = cjson.encode({
          MatchId = matchId,
          UserIds = members
        })
        
        redis.call('RPUSH', outbox, payload)
        
        return members
        """;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Assembler started. Queue={Queue} BatchSize={BatchSize}", _options.QueueKey,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await DoWorkAsync())
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RedisException rex)
            {
                logger.LogError(rex, "Redis error in assembler loop.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in assembler loop.");
            }

            await Task.Delay(_options.IdleDelay, stoppingToken);
        }

        logger.LogInformation("Assembler stopping.");
    }

    private async Task<bool> DoWorkAsync()
    {
        var matchId = Guid.NewGuid().ToString();
        var result = await _db.ScriptEvaluateAsync(
                PopFullBatchLua,
                [_options.QueueKey, _options.OutboxKey],
                [_options.BatchSize, matchId],
                flags: CommandFlags.DemandMaster)
            .ConfigureAwait(false);

        var membersResults = (RedisResult[]?)result;
        var members = membersResults?.Select(x => x.ToString()).ToArray();

        if (members is null || members.Length == 0)
        {
            return false;
        }

        var userIds = members.Select(Guid.Parse).ToArray();

        logger.LogInformation("Match assembled with id: {MatchId}. users: {UserIds}", matchId,
            string.Join(",", userIds));

        return true;
    }
}