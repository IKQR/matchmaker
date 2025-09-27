using System.Text.Json;
using MassTransit;
using MatchMaking.Contracts;
using MatchMaking.Worker.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MatchMaking.Worker.Workers;

public sealed class Outboxer(
    ILogger<Outboxer> logger,
    IConnectionMultiplexer redis,
    IServiceScopeFactory scopedClientFactory,
    IOptions<OutboxerOptions> options) : BackgroundService, IAsyncDisposable
{
    private readonly IDatabase _database = redis.GetDatabase();
    private readonly OutboxerOptions _options = options.Value;
    private readonly AsyncServiceScope _scope = scopedClientFactory.CreateAsyncScope();
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("JsonOutboxer started. Outbox={Outbox}", _options.OutboxListKey);

        var producer = _scope.ServiceProvider.GetRequiredService<ITopicProducer<MatchmakingCompleteMessage>>();
        
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var br = await _database.ListRightPopAsync(_options.OutboxListKey);
                
                if (br.IsNullOrEmpty)
                {
                    await Task.Delay(_options.PopTimeout, ct);
                    continue;
                }

                var json = (string)br!;
                MatchmakingCompleteMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<MatchmakingCompleteMessage>(json, JsonSerializerOptions);
                    if (message is null || Guid.Empty == message.MatchId)
                        throw new FormatException("Envelope is null or matchId empty.");
                }
                catch (Exception parseEx)
                {
                    logger.LogWarning(parseEx, "Broken JSON in outbox. Moving to dead list.");
                    await _database.ListLeftPushAsync(_options.DeadLetterListKey, json);
                    await Task.Delay(_options.IdleDelay, ct);
                    continue;
                }

                try
                {
                    await producer.Produce(message, ct).ConfigureAwait(false);

                    logger.LogInformation("Sent match {MatchId} -> Kafka. players=[{Players}]",
                        message.MatchId, string.Join(",", message.UserIds));
                }
                catch (Exception sendEx)
                {
                    logger.LogWarning(sendEx, "Send failed for {MatchId}. Requeueing at tail.", message.MatchId);

                    await _database.ListRightPushAsync(_options.OutboxListKey, json).ConfigureAwait(false);
                    await Task.Delay(_options.IdleDelay, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error in JsonOutboxer loop.");
                await Task.Delay(_options.IdleDelay, ct);
            }
        }

        logger.LogInformation("JsonOutboxer stopping.");
    }

    private async ValueTask DisposeAsyncCore()
    {
        await _scope.DisposeAsync();
        await redis.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }
}