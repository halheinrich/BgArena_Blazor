using BgTournament.Api;

namespace BgArena_Blazor.Services;

/// <summary>
/// The match- and game-level context a single board position needs to render,
/// decoupled from where that context is sourced. It carries the match-level
/// facts (engine names, match length — 0 being the money-session sentinel) and
/// the game-level facts of the game the position belongs to (the scores
/// entering that game, and whether it is the Crawford game).
///
/// <para>The settled replay builds it from a <see cref="MatchGamesResponse"/>
/// plus the <see cref="GameReplay"/> being stepped; the live feed builds it
/// from the <see cref="MatchSummary"/> plus the snapshot / game-started event
/// that put the current game in view. Six identical facts either way, so
/// <see cref="ReplayDiagramMapper"/> keeps one mapping path regardless of
/// source.</para>
/// </summary>
/// <param name="EngineOne">Engine occupying seat One — the diagram's fixed positive/on-roll side.</param>
/// <param name="EngineTwo">Engine occupying seat Two.</param>
/// <param name="MatchLength">Match length in points; 0 is a money session.</param>
/// <param name="SeatOneScore">Seat One's match score entering the game in view.</param>
/// <param name="SeatTwoScore">Seat Two's match score entering the game in view.</param>
/// <param name="IsCrawford">True iff the game in view is the Crawford game.</param>
public sealed record DiagramContext(
    string EngineOne,
    string EngineTwo,
    int MatchLength,
    int SeatOneScore,
    int SeatTwoScore,
    bool IsCrawford)
{
    /// <summary>
    /// The engine occupying <paramref name="seat"/> — the fixed seat-to-name
    /// mapping the whole match renders against (seat One never flips off the
    /// diagram's positive side).
    /// </summary>
    public string EngineName(Seat seat) => seat == Seat.One ? EngineOne : EngineTwo;

    /// <summary>Context for one game of a settled replay payload.</summary>
    public static DiagramContext ForGame(MatchGamesResponse match, GameReplay game) =>
        new(match.EngineOne, match.EngineTwo, match.MatchLength,
            game.SeatOneScore, game.SeatTwoScore, game.IsCrawford);

    /// <summary>
    /// Context for the live game currently in view: the match-level facts come
    /// from the match summary (fetched once when the page opens), the entering
    /// scores and Crawford flag from the live snapshot / game-started event
    /// that put this game in view — both frame-free facts the producer now
    /// carries on the feed, so nothing is reconstructed client-side.
    /// </summary>
    public static DiagramContext ForLiveGame(
        MatchSummary summary, int seatOneScore, int seatTwoScore, bool isCrawford) =>
        new(summary.EngineOne, summary.EngineTwo, summary.MatchLength,
            seatOneScore, seatTwoScore, isCrawford);
}
