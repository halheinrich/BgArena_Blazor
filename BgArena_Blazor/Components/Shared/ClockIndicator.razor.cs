using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// The one clocked-match indicator for table rows: a glyph with the Fischer
/// parameters in its tooltip, rendering nothing for the flat regime — so the
/// "clocked shows an indicator" rule is encoded once, not per table.
/// </summary>
public partial class ClockIndicator
{
    /// <summary>The row's time control; null (the flat regime) renders nothing.</summary>
    [Parameter]
    [EditorRequired]
    public TimeControl? TimeControl { get; set; }
}
