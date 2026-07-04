using BackgammonDiagram_Lib;
using BgTournament.Api;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Services;

/// <summary>
/// The app-side glue from a replay-contract position (<c>BgTournament.Api</c>)
/// onto <see cref="DiagramRequest"/> for the view-only <c>BackgammonDiagram</c>,
/// driven by a source-agnostic <see cref="DiagramContext"/> so the same mapping
/// serves both the settled replay and the live feed. Positions arrive in
/// <b>seat One's frame</b> and are handed to the diagram unchanged: seat One is
/// the diagram's positive ("on roll") side for the whole match, each engine's
/// name anchors to a fixed side, and nothing is ever flipped app-side — the
/// producer already normalized every position (its flip is the system's single
/// re-expression).
/// </summary>
public static class ReplayDiagramMapper
{
    /// <summary>
    /// Maps one recorded decision moment onto a renderable request: a play
    /// entry becomes a checker-style diagram carrying its dice; cube entries
    /// (offer and response both show the same pre-double position) render
    /// cube-style, without dice.
    /// </summary>
    public static DiagramRequest ForEntry(DiagramContext context, GameEntry entry) =>
        entry is PlayEntry play
            ? Map(context, play.State, play.Die1, play.Die2)
            : Map(context, entry.State, die1: null, die2: null);

    /// <summary>
    /// Maps the position a game ended in — the step after its last entry.
    /// Rendered cube-style: the game is over, so there is no decision and no
    /// dice to show.
    /// </summary>
    public static DiagramRequest ForFinalState(DiagramContext context, GamePosition finalState) =>
        Map(context, finalState, die1: null, die2: null);

    private static DiagramRequest Map(
        DiagramContext context, GamePosition position, int? die1, int? die2)
    {
        var builder = new DiagramRequest.Builder
        {
            Mop = [.. position.Board],
            IsCube = die1 is null,
            Dice = die1 is null ? [0, 0] : [die1.Value, die2!.Value],
            CubeSize = position.CubeValue,
            CubeOwner = ToDiagramCubeOwner(position.CubeOwner),
            IsCrawford = context.IsCrawford,
            MatchLength = context.MatchLength,
            // MatchLength == 0 is the diagram's money-game sentinel; the needs
            // fields are never read on that path, so they stay 0 rather than
            // carrying a meaningless negative value.
            OnRollNeeds = context.MatchLength == 0 ? 0 : context.MatchLength - context.SeatOneScore,
            OpponentNeeds = context.MatchLength == 0 ? 0 : context.MatchLength - context.SeatTwoScore,
            OnRollName = context.EngineOne,
            OpponentName = context.EngineTwo,
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
