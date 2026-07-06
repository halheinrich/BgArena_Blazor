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
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"maxGames":null,"seed":42,"timeControl":null,"status":"running","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";

    /// <summary>A completed match — the golden completed row.</summary>
    public const string CompletedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":"2026-07-05T12:30:00+00:00"}""";

    /// <summary>
    /// An interrupted match — the rehydrated-orphan shape: a terminal status
    /// with no end time (the true end died with the server), carrying the
    /// producer's standard reconstruction detail. Only ever produced by journal
    /// rehydration after a server restart.
    /// </summary>
    public const string InterruptedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"interrupted","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"detail":"The server was interrupted while this match was running; the record was reconstructed from its journal.","startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";

    /// <summary>
    /// The producer's whole-replay golden pin, verbatim: all three entry
    /// kinds under their "type" discriminators, seat-keyed cube owner, and
    /// the game-level outcome + finalState.
    /// </summary>
    public const string GoldenReplay =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"status":"completed","games":[{"gameNumber":1,"winner":"seatTwo","resultKind":"single","cubeValue":2,"points":2,"seatOneScore":0,"seatTwoScore":0,"isCrawford":false,"entries":[{"type":"play","die1":3,"die2":1,"moves":[{"from":8,"to":5},{"from":6,"to":5}],"actor":"seatOne","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}},{"type":"cubeOffer","actor":"seatTwo","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}},{"type":"cubeResponse","action":"take","actor":"seatOne","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}}],"finalState":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":2,"cubeOwner":"seatOne"}}]}""";

    /// <summary>A running tournament with one resolved and one unreached ledger row.</summary>
    public const string RunningTournament =
        """{"tournamentId":"tour-1","participants":["Alpha","Beta"],"matchLength":3,"matchesPerPairing":2,"seed":7,"timeControl":null,"status":"running","winner":null,"detail":null,"standings":[{"rank":1,"participant":"Alpha","wins":1,"losses":0,"sonnebornBerger":0},{"rank":2,"participant":"Beta","wins":0,"losses":1,"sonnebornBerger":0}],"matches":[{"index":0,"seatOne":"Alpha","seatTwo":"Beta","seed":11,"matchId":"match-1","status":"completed","winner":"Alpha"},{"index":1,"seatOne":"Beta","seatTwo":"Alpha","seed":12,"matchId":null,"status":null,"winner":null}],"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";
}
