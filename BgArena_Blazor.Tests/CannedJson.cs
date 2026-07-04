namespace BgArena_Blazor.Tests;

/// <summary>
/// Canned wire JSON shared by the page tests, shaped after the producer's
/// golden pins. Convenience fixtures only — the contract itself is proven by
/// the smoke against the real server.
/// </summary>
internal static class CannedJson
{
    /// <summary>Two idle engines — feeds pickers and the engines dashboard.</summary>
    public const string TwoIdleEngines =
        """[{"name":"Alpha","version":"1.0","author":"Hal","inMatch":false},{"name":"Beta","version":null,"author":null,"inMatch":false}]""";

    /// <summary>One idle engine and one mid-match.</summary>
    public const string MixedEngines =
        """[{"name":"Alpha","version":"1.0","author":"Hal","inMatch":false},{"name":"Beta","version":null,"author":null,"inMatch":true}]""";

    /// <summary>A single running match, as POST /matches answers.</summary>
    public const string RunningMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"maxGames":null,"seed":42,"status":"running","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"detail":null}""";

    /// <summary>A completed match — the golden completed row.</summary>
    public const string CompletedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"detail":null}""";

    /// <summary>A running tournament with one resolved and one unreached ledger row.</summary>
    public const string RunningTournament =
        """{"tournamentId":"tour-1","participants":["Alpha","Beta"],"matchLength":3,"matchesPerPairing":2,"seed":7,"status":"running","winner":null,"detail":null,"standings":[{"rank":1,"participant":"Alpha","wins":1,"losses":0,"sonnebornBerger":0},{"rank":2,"participant":"Beta","wins":0,"losses":1,"sonnebornBerger":0}],"matches":[{"index":0,"seatOne":"Alpha","seatTwo":"Beta","seed":11,"matchId":"match-1","status":"completed","winner":"Alpha"},{"index":1,"seatOne":"Beta","seatTwo":"Alpha","seed":12,"matchId":null,"status":null,"winner":null}]}""";
}
