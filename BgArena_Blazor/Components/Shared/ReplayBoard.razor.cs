using BackgammonDiagram_Lib;
using BgDiag_Razor.Components;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// The sized container over the view-only <see cref="BackgammonDiagram"/>.
/// The diagram has no intrinsic size (its SVG is <c>viewBox</c> +
/// <c>width="100%"</c> — a documented pitfall), so this wrapper's
/// max-width-constrained <c>replay-board</c> div is what gives it layout
/// size; every replay render goes through here rather than re-solving sizing
/// at each call site.
/// </summary>
public partial class ReplayBoard
{
    /// <summary>The position to render, already mapped by <c>ReplayDiagramMapper</c>.</summary>
    [Parameter]
    [EditorRequired]
    public DiagramRequest Request { get; set; } = default!;
}
