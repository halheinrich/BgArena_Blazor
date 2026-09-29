using BackgammonDiagram_Lib;
using BgTournament.Api;
using ApiCubeOwner = BgTournament.Api.CubeOwner;
using BoardPosition = BgDataTypes_Lib.BoardPosition;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Services;

/// <summary>
/// The app-side glue from a replay-contract position (<c>BgTournament.Api</c>)
/// onto a board request (<see cref="DiagramRequest.ForBoard"/>) for the
/// view-only <c>BackgammonDiagram</c>, driven by a source-agnostic
/// <see cref="DiagramContext"/> so the same mapping serves both the settled
/// replay and the live feed. Positions arrive in <b>seat One's frame</b> and
/// are handed to the diagram unchanged: seat One is the diagram's positive
/// ("on roll") side for the whole match, each engine's name anchors to a fixed
/// side, and nothing is ever flipped app-side — the producer already
/// normalized every position (its flip is the system's single re-expression).
/// </summary>
/// <remarks>
/// Every arena board is a board, never a decision: the diagram draws the
/// served position and the display facts stated here, and nothing of a
/// decision (no <c>"Cube Action?"</c>, no analysis). The facts are the API's,
/// stated as served — the game's Crawford flag on a match board, and on a
/// money board no Jacoby rule, which the API does not state. The diagram words
/// them (and double match point, from the away scores) by the same rule it
/// words a decision's.
/// </remarks>
public static class ReplayDiagramMapper
{
    /// <summary>
    /// Maps one recorded decision moment onto a board request: a play entry
    /// shows the dice rolled, in the served order; cube entries (offer and
    /// response both show the same pre-double position) show no dice.
    /// </summary>
    /// <exception cref="ArgumentException">The served position cannot be drawn: a malformed board, or a die outside 1–6.</exception>
    public static DiagramRequest ForEntry(DiagramContext context, GameEntry entry) =>
        entry is PlayEntry play
            ? Map(context, play.State, new DiceFaces(play.Die1, play.Die2))
            : Map(context, entry.State, dice: null);

    /// <summary>
    /// Maps the position a game ended in — the step after its last entry. The
    /// game is over, so it shows no dice.
    /// </summary>
    /// <exception cref="ArgumentException">The served position cannot be drawn: a malformed board.</exception>
    public static DiagramRequest ForFinalState(DiagramContext context, GamePosition finalState) =>
        Map(context, finalState, dice: null);

    private static DiagramRequest Map(DiagramContext context, GamePosition position, DiceFaces? dice) =>
        DiagramRequest.ForBoard(
            new BoardPosition([.. position.Board]),
            new DisplayFacts
            {
                OnRollName = context.EngineOne,
                OpponentName = context.EngineTwo,
                Dice = dice,
                CubeValue = position.CubeValue,
                CubeOwner = ToDiagramCubeOwner(position.CubeOwner),
                Score = ToRailScore(context),
            });

    /// <summary>
    /// The score the rails show, from the API's facts alone: a match's away
    /// scores entering the game in view, with the game's Crawford flag; for a
    /// money session (the API's match length 0), the money label with no
    /// Jacoby rule, since the API states none.
    /// </summary>
    private static RailScore ToRailScore(DiagramContext context) =>
        context.MatchLength == 0
            ? new MoneyRailScore(isJacoby: null)
            : new MatchRailScore(
                onRollNeeds: context.MatchLength - context.SeatOneScore,
                opponentNeeds: context.MatchLength - context.SeatTwoScore,
                isCrawford: context.IsCrawford);

    private static DiagramCubeOwner ToDiagramCubeOwner(ApiCubeOwner owner) => owner switch
    {
        ApiCubeOwner.Centered => DiagramCubeOwner.Centered,
        // Seat One is the diagram's positive/on-roll side — the fixed-frame rule.
        ApiCubeOwner.SeatOne => DiagramCubeOwner.OnRoll,
        ApiCubeOwner.SeatTwo => DiagramCubeOwner.Opponent,
        _ => throw new ArgumentOutOfRangeException(nameof(owner), owner, "Unknown cube owner."),
    };
}
