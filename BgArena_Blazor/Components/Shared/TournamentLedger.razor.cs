using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Renders a tournament's schedule ledger, one
/// <see cref="TournamentMatchEntry"/> per row in schedule order. Rows the
/// tournament has reached carry a hosted-match id that links to the match
/// detail page (and from there to the replay); unreached rows render as
/// scheduled. Display-only.
/// </summary>
public partial class TournamentLedger
{
    /// <summary>The schedule ledger to render, in schedule order.</summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<TournamentMatchEntry> Entries { get; set; } = [];
}
