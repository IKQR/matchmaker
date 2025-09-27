using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Caching.Distributed;
using MassTransit;
using MatchMaking.Contracts;
using MatchMaking.Service.Cache;

namespace MatchMaking.Service;

[ApiController]
[Route("v1/matchmaking")]
[OutputCache(PolicyName = MatchmakingOutputCachePolicy.PolicyName)]
public class Controller(ITopicProducer<MatchmakingRequestMessage> producer, IDistributedCache cache) : ControllerBase
{
    private const string CorrelationIdHeader = "X-Correlation-ID";

    /// <summary>
    /// Publishes a matchmaking request for a specific user and returns a 202 Accepted response if successful.
    /// </summary>
    /// <param name="userId">The unique identifier of the user initiating the matchmaking request.</param>
    /// <param name="correlationId">The unique correlation ID for tracking the request across systems, provided in the header.</param>
    /// <returns>An IActionResult that indicates the status of the request. Returns a 202 Accepted response if the request is successfully published.</returns>
    [HttpPost("{userId}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [OutputCache(VaryByRouteValueNames = ["userId"], VaryByHeaderNames = [CorrelationIdHeader], Duration = 3)]
    public async Task<IActionResult> CreateMatch(
        [FromRoute] Guid userId,
        [FromHeader(Name = CorrelationIdHeader)]
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        await producer.Produce(
            new MatchmakingRequestMessage(userId),
            Pipe.Execute<KafkaSendContext<MatchmakingRequestMessage>>(ctx => ctx.MessageId = correlationId),
            cancellationToken);

        return Accepted();
    }

    /// <summary>
    /// Retrieves the match ID for a specific user if it exists in the distributed cache.
    /// </summary>
    /// <param name="userId">The unique identifier of the user whose match details are being requested.</param>
    /// <returns>An IActionResult that contains the match ID if found (200 OK), or a 404 Not Found response if no match is associated with the given user.</returns>
    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(MatchmakingCompleteMessage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    [OutputCache(VaryByRouteValueNames = ["userId"], Duration = 60)]
    public async Task<IActionResult> GetMatch([FromRoute] string userId, CancellationToken cancellationToken)
    {
        var matchId = await cache.GetStringAsync("user:" + userId, token: cancellationToken);

        if (string.IsNullOrEmpty(matchId))
        {
            return NotFound();
        }

        var match = await cache.GetStringAsync("match:" + matchId, token: cancellationToken);
        if (string.IsNullOrEmpty(match))
        {
            return NotFound();
        }
        
        var matchObject = JsonSerializer.Deserialize<MatchmakingCompleteMessage>(match);

        if (matchObject == null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        return Ok(matchObject);
    }
}