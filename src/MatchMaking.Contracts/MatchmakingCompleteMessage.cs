namespace MatchMaking.Contracts;

public record MatchmakingCompleteMessage(Guid MatchId, Guid[] UserIds);