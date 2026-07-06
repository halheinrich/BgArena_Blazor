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
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"maxGames":null,"seed":42,"timeControl":null,"status":"running","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"forfeitCause":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";

    /// <summary>A completed match — the golden completed row.</summary>
    public const string CompletedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"forfeitCause":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":"2026-07-05T12:30:00+00:00"}""";

    /// <summary>
    /// A completed match that ran under a Fischer clock — the clocked-match
    /// indicator and detail-card time-control fixtures.
    /// </summary>
    public const string ClockedCompletedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":{"initialSeconds":120,"incrementSeconds":8},"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"forfeitCause":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":"2026-07-05T12:30:00+00:00"}""";

    /// <summary>
    /// A forfeited match carrying the structured cause beside the prose detail
    /// — the forfeit-cause pill fixture (flag fall: a clocked match whose pool
    /// emptied mid-decision).
    /// </summary>
    public const string ForfeitedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":{"initialSeconds":120,"incrementSeconds":8},"status":"forfeited","winner":"Beta","seatOneScore":null,"seatTwoScore":null,"forfeitedBy":"Alpha","forfeitCause":"flagFall","detail":"Engine 'Alpha' ran out of time on a play query (flag fall).","startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":"2026-07-05T12:30:00+00:00"}""";

    /// <summary>
    /// An interrupted match — the rehydrated-orphan shape: a terminal status
    /// with no end time (the true end died with the server), carrying the
    /// producer's standard reconstruction detail. Only ever produced by journal
    /// rehydration after a server restart.
    /// </summary>
    public const string InterruptedMatch =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"interrupted","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"forfeitCause":null,"detail":"The server was interrupted while this match was running; the record was reconstructed from its journal.","startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";

    /// <summary>
    /// The producer's whole-replay golden pin, verbatim: all three entry
    /// kinds under their "type" discriminators, seat-keyed cube owner, and
    /// the game-level outcome + finalState.
    /// </summary>
    public const string GoldenReplay =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"status":"completed","games":[{"gameNumber":1,"winner":"seatTwo","resultKind":"single","cubeValue":2,"points":2,"seatOneScore":0,"seatTwoScore":0,"isCrawford":false,"entries":[{"type":"play","die1":3,"die2":1,"moves":[{"from":8,"to":5},{"from":6,"to":5}],"actor":"seatOne","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}},{"type":"cubeOffer","actor":"seatTwo","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}},{"type":"cubeResponse","action":"take","actor":"seatOne","state":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":1,"cubeOwner":"centered"}}],"finalState":{"board":[0,-2,0,0,0,0,5,0,3,0,0,0,-5,5,0,0,0,-3,0,-5,0,0,0,0,2,0],"cubeValue":2,"cubeOwner":"seatOne"}}]}""";

    /// <summary>
    /// The producer's whole-audit-timeline golden pin, verbatim: every event
    /// kind under its "type" discriminator, the fair-dice verification packet
    /// (created carries commitment + algorithm, terminal the revealed key),
    /// and the clock arithmetic as JSON numbers.
    /// </summary>
    public const string GoldenAudit =
        """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"completed","integrity":null,"events":[{"type":"created","at":"2026-07-05T12:00:00+00:00","matchLength":3,"maxGames":null,"seed":42,"diceAlgorithm":"hmac-sha256-dice-v1","diceCommitment":"ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100","timeControl":{"initialSeconds":120,"incrementSeconds":8}},{"type":"started","at":"2026-07-05T12:00:00+00:00"},{"type":"gameStarted","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"seatOneScore":0,"seatTwoScore":0,"isCrawford":false},{"type":"clock","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"seat":"seatOne","decision":"play","thinkSeconds":12.5,"incrementCredited":true,"remainingBeforeSeconds":120,"remainingAfterSeconds":115.5},{"type":"play","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"entryIndex":0,"actor":"seatOne","die1":3,"die2":1},{"type":"cubeOffer","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"entryIndex":1,"actor":"seatTwo"},{"type":"cubeResponse","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"entryIndex":2,"actor":"seatOne","action":"take"},{"type":"lateReply","at":"2026-07-05T12:00:00+00:00","seat":"seatTwo","requestId":"q-9"},{"type":"gameEnded","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"winner":"seatTwo","resultKind":"single","cubeValue":2},{"type":"terminal","at":"2026-07-05T12:30:00+00:00","status":"completed","winner":"Beta","seatOneScore":1,"seatTwoScore":3,"forfeitedBy":null,"forfeitCause":null,"detail":null,"diceAlgorithm":"hmac-sha256-dice-v1","diceKey":"00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff"}]}""";

    /// <summary>A running tournament with one resolved and one unreached ledger row.</summary>
    public const string RunningTournament =
        """{"tournamentId":"tour-1","participants":["Alpha","Beta"],"matchLength":3,"matchesPerPairing":2,"seed":7,"timeControl":null,"status":"running","winner":null,"detail":null,"standings":[{"rank":1,"participant":"Alpha","wins":1,"losses":0,"sonnebornBerger":0},{"rank":2,"participant":"Beta","wins":0,"losses":1,"sonnebornBerger":0}],"matches":[{"index":0,"seatOne":"Alpha","seatTwo":"Beta","seed":11,"matchId":"match-1","status":"completed","winner":"Alpha"},{"index":1,"seatOne":"Beta","seatTwo":"Alpha","seed":12,"matchId":null,"status":null,"winner":null}],"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";
}
