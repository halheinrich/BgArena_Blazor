using BackgammonDiagram_Lib;
using BgTournament.Api;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Services;

/// <summary>
/// The app-side glue from the replay contract (<c>BgTournament.Api</c>) onto
/// <see cref="DiagramRequest"/> for the view-only <c>BackgammonDiagram</c>.
/// Replay positions arrive in <b>seat One's frame</b> and are handed to the
/// diagram unchanged: seat One is the diagram's positive ("on roll") side for
/// the whole match, each engine's name anchors to a fixed side, and nothing
/// is ever flipped app-side — the producer already normalized every position
/// (its flip is the system's single re-expression).
/// </summary>
public static class ReplayDiagramMapper
{
    /// <summary>
    /// Maps one recorded decision moment onto a renderable request: a play
    /// entry becomes a checker-style diagram carrying its dice; cube entries
    /// (offer and response both show the same pre-double position) render
    /// cube-style, without dice.
    /// </summary>
    public static DiagramRequest ForEntry(MatchGamesResponse match, GameReplay game, GameEntry entry) =>
        entry is PlayEntry play
            ? Map(match, game, play.State, play.Die1, play.Die2)
            : Map(match, game, entry.State, die1: null, die2: null);

    /// <summary>
    /// Maps the position a game ended in — the step after its last entry.
    /// Rendered cube-style: the game is over, so there is no decision and no
    /// dice to show.
    /// </summary>
    public static DiagramRequest ForFinalState(MatchGamesResponse match, GameReplay game) =>
        Map(match, game, game.FinalState, die1: null, die2: null);

    private static DiagramRequest Map(
        MatchGamesResponse match, GameReplay game, GamePosition position, int? die1, int? die2)
    {
        var builder = new DiagramRequest.Builder
        {
            Mop = [.. position.Board],
            IsCube = die1 is null,
            Dice = die1 is null ? [0, 0] : [die1.Value, die2!.Value],
            CubeSize = position.CubeValue,
            CubeOwner = ToDiagramCubeOwner(position.CubeOwner),
            IsCrawford = game.IsCrawford,
            MatchLength = match.MatchLength,
            // MatchLength == 0 is the diagram's money-game sentinel; the needs
            // fields are never read on that path, so they stay 0 rather than
            // carrying a meaningless negative value.
            OnRollNeeds = match.MatchLength == 0 ? 0 : match.MatchLength - game.SeatOneScore,
            OpponentNeeds = match.MatchLength == 0 ? 0 : match.MatchLength - game.SeatTwoScore,
            OnRollName = match.EngineOne,
            OpponentName = match.EngineTwo,
            Mode = DiagramMode.Problem,
        };
        return builder.Build();
    }

    private static DiagramCubeOwner ToDiagramCubeOwner(CubeOwner owner) => owner switch
    {
        CubeOwner.Centered => DiagramCubeOwner.Centered,
        // Seat One is the diagram's positive/on-roll side — the fixed-frame rule.
        CubeOwner.SeatOne => DiagramCubeOwner.OnRoll,
        CubeOwner.SeatTwo => DiagramCubeOwner.Opponent,
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown cube owner."),
    };
}
