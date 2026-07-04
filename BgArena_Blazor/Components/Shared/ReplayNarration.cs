using BgArena_Blazor.Services;
using BgTournament.Api;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Single home of the per-entry caption wording shared by the step-through
/// replay and the live feed — who did what: a play names the roll and prints
/// its moves verbatim in the actor's own numbering (the contract's two
/// sentinels, from 25 = bar and to 0 = off, given their standard notation
/// names); a cube offer names the new value; a response takes or passes. Both
/// surfaces render the same <see cref="GameEntry"/> shape, so this wording is
/// single-sourced here rather than duplicated per viewer. The actor's engine
/// name is resolved through the <see cref="DiagramContext"/> the surface
/// already holds.
/// </summary>
internal static class ReplayNarration
{
    /// <summary>Names the actor and action of one recorded decision moment.</summary>
    public static string Entry(DiagramContext context, GameEntry entry) => entry switch
    {
        PlayEntry play =>
            $"{context.EngineName(play.Actor)} rolls {play.Die1}-{play.Die2}: {MovesText(play.Moves)}",
        CubeOfferEntry offer =>
            $"{context.EngineName(offer.Actor)} doubles to {offer.State.CubeValue * 2}",
        CubeResponseEntry { Action: CubeResponseAction.Take } response =>
            $"{context.EngineName(response.Actor)} takes",
        CubeResponseEntry response =>
            $"{context.EngineName(response.Actor)} passes",
        _ => throw new InvalidOperationException($"Unknown replay entry kind '{entry.GetType().Name}'."),
    };

    /// <summary>
    /// Moves stay in the actor's own numbering, printed verbatim — never
    /// interpreted. The contract's two sentinels get their standard notation
    /// names: from 25 enters off the actor's bar, to 0 bears the checker off.
    /// </summary>
    private static string MovesText(IReadOnlyList<PlayMove> moves) =>
        moves.Count == 0
            ? "no play"
            : string.Join(" ", moves.Select(move =>
                $"{(move.From == 25 ? "bar" : move.From.ToString())}/{(move.To == 0 ? "off" : move.To.ToString())}"));
}
