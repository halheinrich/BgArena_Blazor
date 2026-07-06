using System.Globalization;
using BgTournament.Api;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Single home of the audit-timeline wording — the audit surface's sibling
/// of <see cref="ReplayNarration"/>: each event's wire kind (the row's type
/// tag and CSS key), its one-line caption naming the actor and the recorded
/// fact, and the replay-join reference. Wording only, facts verbatim: the
/// packet's documented correlation semantics (clock precedes the decision it
/// timed, and so on) surface as the static <see cref="ClockCorrelationNote"/>
/// legend — never as computed clock→decision joins or think-time aggregates,
/// which would re-encode the producer's ordering contract app-side.
/// </summary>
internal static class AuditNarration
{
    /// <summary>
    /// The producer's documented clock-correlation semantics, surfaced as a
    /// presentational legend on clocked timelines.
    /// </summary>
    public const string ClockCorrelationNote =
        "Reading the clock rows: a clock row precedes the decision it timed; "
        + "a cube-offer clock row with no cube row before the next clock row "
        + "was a declined double window; a clock row with no decision after "
        + "it timed the decision that ended the match; a play row with no "
        + "clock row was a dance (the forced empty play was never queried).";

    /// <summary>The event's wire discriminator — the row's type tag; lowercase it for CSS.</summary>
    public static string Kind(AuditEvent auditEvent) => auditEvent switch
    {
        AuditCreatedEvent => "created",
        AuditStartedEvent => "started",
        AuditGameStartedEvent => "gameStarted",
        AuditPlayEvent => "play",
        AuditCubeOfferEvent => "cubeOffer",
        AuditCubeResponseEvent => "cubeResponse",
        AuditGameEndedEvent => "gameEnded",
        AuditClockEvent => "clock",
        AuditLateReplyEvent => "lateReply",
        AuditTerminalEvent => "terminal",
        _ => throw new InvalidOperationException($"Unknown audit event kind '{auditEvent.GetType().Name}'."),
    };

    /// <summary>
    /// The row's UTC time of day; an em-dash when the event honestly has none
    /// (an Interrupted match's terminal event — its true end time died with
    /// the server).
    /// </summary>
    public static string TimeText(DateTimeOffset? at) =>
        at?.UtcDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "—";

    /// <summary>Names the actor and the recorded fact of one audit event.</summary>
    public static string Caption(MatchAuditResponse audit, AuditEvent auditEvent) => auditEvent switch
    {
        AuditCreatedEvent created => CreatedCaption(created),
        AuditStartedEvent => "Wire start — matchStarted sent to both engines",
        AuditGameStartedEvent game =>
            $"Game {game.GameNumber} — {audit.EngineOne} {game.SeatOneScore} · {audit.EngineTwo} {game.SeatTwoScore}"
            + (game.IsCrawford ? " · Crawford game" : ""),
        AuditPlayEvent play => $"{EngineName(audit, play.Actor)} rolls {play.Die1}-{play.Die2}",
        AuditCubeOfferEvent offer => $"{EngineName(audit, offer.Actor)} offers a double",
        AuditCubeResponseEvent { Action: CubeResponseAction.Take } take =>
            $"{EngineName(audit, take.Actor)} takes",
        AuditCubeResponseEvent pass => $"{EngineName(audit, pass.Actor)} passes",
        AuditGameEndedEvent ended =>
            $"Game {ended.GameNumber} — {EngineName(audit, ended.Winner)} wins a {ResultKindText(ended.ResultKind)} at cube {ended.CubeValue}",
        AuditClockEvent clock => ClockCaption(audit, clock),
        AuditLateReplyEvent late =>
            $"{EngineName(audit, late.Seat)} answered an abandoned query too late — reply discarded (request {late.RequestId})",
        AuditTerminalEvent terminal => TerminalCaption(terminal),
        _ => throw new InvalidOperationException($"Unknown audit event kind '{auditEvent.GetType().Name}'."),
    };

    /// <summary>
    /// The replay-join reference rendered beside a row: decision events join
    /// to the replay surface by game number + 0-based entry index (the
    /// producer contract); a clock event carries only its game. Null for
    /// events whose caption already names the game, and for match-level ones.
    /// </summary>
    public static string? Reference(AuditEvent auditEvent) => auditEvent switch
    {
        AuditPlayEvent play => JoinText(play.GameNumber, play.EntryIndex),
        AuditCubeOfferEvent offer => JoinText(offer.GameNumber, offer.EntryIndex),
        AuditCubeResponseEvent response => JoinText(response.GameNumber, response.EntryIndex),
        AuditClockEvent clock => $"game {clock.GameNumber}",
        _ => null,
    };

    private static string JoinText(int gameNumber, int entryIndex) =>
        $"game {gameNumber} · entry {entryIndex}";

    private static string EngineName(MatchAuditResponse audit, Seat seat) =>
        seat == Seat.One ? audit.EngineOne : audit.EngineTwo;

    /// <summary>
    /// The configuration line. Fair mode names the algorithm (the recorded
    /// seed deliberately stays unnamed there — it does not drive fair dice
    /// and must not read as a reproduction key); an explicit-seed match names
    /// its seed.
    /// </summary>
    private static string CreatedCaption(AuditCreatedEvent created)
    {
        string length = ArenaDisplay.LengthText(created.MatchLength, created.MaxGames);
        string cap = created is { MatchLength: not 0, MaxGames: int maxGames } ? $", cap {maxGames} games" : "";
        string dice = created.DiceAlgorithm is { } algorithm
            ? $", fair dice ({algorithm})"
            : $", seed {created.Seed}";
        return $"Match created — {length}{cap}, {ArenaDisplay.TimeControlText(created.TimeControl)}{dice}";
    }

    /// <summary>The clock arithmetic, verbatim (invariant number formatting).</summary>
    private static string ClockCaption(MatchAuditResponse audit, AuditClockEvent clock)
    {
        string think = clock.ThinkSeconds.ToString(CultureInfo.InvariantCulture);
        string before = clock.RemainingBeforeSeconds.ToString(CultureInfo.InvariantCulture);
        string after = clock.RemainingAfterSeconds.ToString(CultureInfo.InvariantCulture);
        string credit = clock.IncrementCredited ? "increment credited" : "no increment credited";
        return $"{EngineName(audit, clock.Seat)} — {DecisionText(clock.Decision)} decision: "
            + $"{think}s think, pool {before}s → {after}s, {credit}";
    }

    private static string TerminalCaption(AuditTerminalEvent terminal)
    {
        string score = terminal is { SeatOneScore: int one, SeatTwoScore: int two } ? $" {one}–{two}" : "";
        return terminal.Status switch
        {
            MatchStatus.Completed when terminal.Winner is { } winner =>
                $"Match completed — {winner} wins{score}",
            MatchStatus.Completed => $"Match completed{(score.Length == 0 ? "" : " —" + score)}",
            MatchStatus.Forfeited =>
                $"Match forfeited by {terminal.ForfeitedBy}"
                + (terminal.ForfeitCause is { } cause ? $" — {ArenaDisplay.ForfeitCauseText(cause)}" : "")
                + (terminal.Winner is { } winner ? $"; {winner} wins" : ""),
            MatchStatus.Aborted => "Match aborted by the server",
            MatchStatus.Faulted => "Match faulted — an unexpected server-side error",
            MatchStatus.Interrupted =>
                "Match interrupted — the server died while it was running; the record was reconstructed from its journal",
            _ => throw new InvalidOperationException($"Unexpected terminal audit status '{terminal.Status}'."),
        };
    }

    private static string DecisionText(DecisionKind decision) => decision switch
    {
        DecisionKind.Play => "play",
        DecisionKind.CubeOffer => "cube offer",
        DecisionKind.CubeResponse => "cube response",
        _ => throw new InvalidOperationException($"Unknown decision kind '{decision}'."),
    };

    private static string ResultKindText(GameResultKind kind) => kind switch
    {
        GameResultKind.Single => "single",
        GameResultKind.Gammon => "gammon",
        GameResultKind.Backgammon => "backgammon",
        _ => throw new InvalidOperationException($"Unknown game result kind '{kind}'."),
    };
}
