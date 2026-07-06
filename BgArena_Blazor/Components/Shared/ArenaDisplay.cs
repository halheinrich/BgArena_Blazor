using System.Globalization;
using BgTournament.Api;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Single home of the small presentation rules shared across pages and
/// tables, so no formatting decision is encoded twice: the status → CSS-class
/// mapping, the match-length wording (0 is the money-session sentinel), the
/// score line, the timestamp/duration formats (absolute UTC, deliberately —
/// relative wording would depend on the wall clock), the time-control
/// wording (null is the flat per-decision regime), and the structured
/// forfeit-cause wording and pill CSS.
/// </summary>
internal static class ArenaDisplay
{
    /// <summary>CSS classes for a match-status pill.</summary>
    public static string StatusCss(MatchStatus status) =>
        $"status status-{status.ToString().ToLowerInvariant()}";

    /// <summary>CSS classes for a tournament-status pill.</summary>
    public static string StatusCss(TournamentStatus status) =>
        $"status status-{status.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Human wording for a match's length: points for match play, the games
    /// cap for a money session (<paramref name="matchLength"/> 0).
    /// </summary>
    public static string LengthText(int matchLength, int? maxGames) =>
        matchLength == 0 ? $"money · {maxGames} games"
        : matchLength == 1 ? "1 point"
        : $"{matchLength} points";

    /// <summary>The final score line, or an em-dash while there is none.</summary>
    public static string ScoreText(MatchSummary match) =>
        match is { SeatOneScore: int one, SeatTwoScore: int two } ? $"{one}–{two}" : "—";

    /// <summary>
    /// The kind of contest, for surfaces that don't carry a games cap (the
    /// replay envelope): match play by its length, or a money session.
    /// </summary>
    public static string MatchKindText(int matchLength) =>
        matchLength == 0 ? "money session" : $"{matchLength}-point match";

    /// <summary>
    /// The note for a partial replay — a terminal match that did not complete
    /// naturally (<paramref name="status"/> other than
    /// <see cref="MatchStatus.Completed"/>) serves only the games that finished
    /// before the break. The count is sourced from the served payload, not
    /// re-derived.
    /// </summary>
    public static string PartialReplayText(MatchStatus status, int gameCount) =>
        $"{status} — showing the {gameCount} completed "
        + (gameCount == 1 ? "game" : "games")
        + " that finished before the match ended.";

    /// <summary>
    /// A UTC instant at minute precision (<c>2026-07-05 12:00</c>). No zone
    /// suffix — the surface labels the UTC convention once (a column header
    /// or a card label), not per value.
    /// </summary>
    public static string TimestampText(DateTimeOffset utc) =>
        utc.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// An elapsed span in human units, largest two only
    /// (<c>34s</c>, <c>12m 34s</c>, <c>1h 2m</c>, <c>2d 3h</c>).
    /// </summary>
    public static string DurationText(DateTimeOffset startedAtUtc, DateTimeOffset endedAtUtc)
    {
        TimeSpan span = endedAtUtc - startedAtUtc;
        return span switch
        {
            { TotalDays: >= 1 } => $"{(int)span.TotalDays}d {span.Hours}h",
            { TotalHours: >= 1 } => $"{(int)span.TotalHours}h {span.Minutes}m",
            { TotalMinutes: >= 1 } => $"{(int)span.TotalMinutes}m {span.Seconds}s",
            _ => $"{Math.Max(0, (int)span.TotalSeconds)}s",
        };
    }

    /// <summary>
    /// The dashboard "when" cell: the start instant, joined by the duration
    /// once the record carries an end time (a running record — or an
    /// Interrupted one, whose end time died with the server — shows only the
    /// start).
    /// </summary>
    public static string WhenText(DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc) =>
        endedAtUtc is { } ended
            ? $"{TimestampText(startedAtUtc)} · {DurationText(startedAtUtc, ended)}"
            : TimestampText(startedAtUtc);

    /// <summary>
    /// The detail-card "ended" line: the end instant plus the duration when
    /// recorded; an em-dash while the record is still running; the honest
    /// lost-with-the-server wording for a terminal record with no end time
    /// (an Interrupted one).
    /// </summary>
    public static string EndedText(bool isTerminal, DateTimeOffset startedAtUtc, DateTimeOffset? endedAtUtc) =>
        endedAtUtc is { } ended ? $"{TimestampText(ended)} · {DurationText(startedAtUtc, ended)}"
        : isTerminal ? "unknown — the end time died with the server"
        : "—";

    /// <summary>
    /// The time-control wording: the Fischer parameters when clocked (the
    /// producer's own shorthand), or the flat per-decision-timeout regime
    /// when null.
    /// </summary>
    public static string TimeControlText(TimeControl? timeControl) =>
        timeControl is null ? "flat per-decision timeout" : $"Fischer {timeControl}";

    /// <summary>Human wording for the structured forfeit cause.</summary>
    public static string ForfeitCauseText(ForfeitCause cause) => cause switch
    {
        ForfeitCause.ContractViolation => "contract violation",
        ForfeitCause.Timeout => "timeout",
        ForfeitCause.FlagFall => "flag fall",
        ForfeitCause.Disconnect => "disconnect",
        ForfeitCause.NeverConnected => "never connected",
        _ => throw new InvalidOperationException($"Unknown forfeit cause '{cause}'."),
    };

    /// <summary>CSS classes for a forfeit-cause pill.</summary>
    public static string ForfeitCauseCss(ForfeitCause cause) =>
        $"cause cause-{cause.ToString().ToLowerInvariant()}";
}
