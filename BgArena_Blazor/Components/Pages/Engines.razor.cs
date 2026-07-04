using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// The landing dashboard: every connected engine with its claimed/idle
/// state, polling-refreshed.
/// </summary>
public partial class Engines
{
    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>Latest engine listing; null until the first load answers.</summary>
    protected IReadOnlyList<EngineSummary>? EngineRows { get; private set; }

    /// <inheritdoc />
    protected override async Task RefreshAsync(CancellationToken cancellationToken) =>
        EngineRows = await Arena.GetEnginesAsync(cancellationToken);
}
