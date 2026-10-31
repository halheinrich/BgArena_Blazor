using BackgammonDiagram_Lib;
using BgDiag_Razor.Components;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// The sized container over the view-only <see cref="BackgammonDiagram"/>.
/// The diagram takes its size from its host: it fills the width it is given
/// and keeps the board's ratio (BgDiag_Razor, "Sizing: the ratio, contain-fit
/// and the board slot"). This wrapper's max-width-constrained
/// <c>replay-board</c> div is that host; every replay and live render goes
/// through here rather than re-solving sizing at each call site.
/// </summary>
public partial class ReplayBoard
{
    /// <summary>The position to render, already mapped by <c>ReplayDiagramMapper</c>.</summary>
    [Parameter]
    [EditorRequired]
    public DiagramRequest Request { get; set; } = default!;
}
