using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// The one rendering path for <see cref="MatchSummary"/> rows — the list page
/// and any future row consumer share it (the producer pins list and by-id
/// rows byte-identical, so one renderer is the honest shape). Each row links
/// to its match detail page. Display-only: navigation is links, no callbacks.
/// </summary>
public partial class MatchesTable
{
    /// <summary>The match rows to render, in the order given.</summary>
    [Parameter]
    [EditorRequired]
    public IReadOnlyList<MatchSummary> Matches { get; set; } = [];
}
