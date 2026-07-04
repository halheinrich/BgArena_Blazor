using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Renders a tournament's standings, one <see cref="StandingEntry"/> per row,
/// in the order given (the server already serves best rank first — a total
/// deterministic order). Display-only.
/// </summary>
public partial class StandingsTable
{
    /// <summary>The standings lines to render, best rank first.</summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<StandingEntry> Standings { get; set; } = [];
}
