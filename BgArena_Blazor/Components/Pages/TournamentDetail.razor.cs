using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// One tournament in full: format, status, point-in-time standings, and the
/// schedule ledger whose reached rows link to their hosted matches. Polls
/// while the tournament is running and pauses once it is terminal (or the id
/// is unknown); navigating to a different id resets and resumes.
/// </summary>
public partial class TournamentDetail
{
    private string? _watchedId;

    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>The tournament id from the route.</summary>
    [Parameter]
    public string TournamentId { get; set; } = string.Empty;

    /// <summary>The latest record; null until the first load answers.</summary>
    protected TournamentSummary? Tournament { get; private set; }

    /// <summary>True when the server answered 404 — no tournament with this id.</summary>
    protected bool NotFound { get; private set; }

    /// <inheritdoc />
    protected override bool ShouldPoll =>
        !NotFound && Tournament is null or { Status: TournamentStatus.Running };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Same component instance, new id (in-app navigation): reset so the
        // paused poll loop picks the new id up on its next tick. The first
        // call after OnInitializedAsync must not wipe the initial load.
        if (_watchedId is not null && _watchedId != TournamentId)
        {
            Tournament = null;
            NotFound = false;
        }
        _watchedId = TournamentId;
    }

    /// <inheritdoc />
    protected override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ArenaResult<TournamentSummary> result = await Arena.GetTournamentAsync(TournamentId, cancellationToken);
        if (result.IsSuccess)
        {
            Tournament = result.Value;
            NotFound = false;
        }
        else
        {
            NotFound = true;
        }
    }
}
