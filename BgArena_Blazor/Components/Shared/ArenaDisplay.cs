using BgTournament.Api;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Single home of the small presentation rules shared across pages and
/// tables, so no formatting decision is encoded twice: the status → CSS-class
/// mapping, the match-length wording (0 is the money-session sentinel), and
/// the score line.
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
        matchLength == 0 ? $"money · {maxGames} games" : $"{matchLength} points";

    /// <summary>The final score line, or an em-dash while there is none.</summary>
    public static string ScoreText(MatchSummary match) =>
        match is { SeatOneScore: int one, SeatTwoScore: int two } ? $"{one}–{two}" : "—";
}
