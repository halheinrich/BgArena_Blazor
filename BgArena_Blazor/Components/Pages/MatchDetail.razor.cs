using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// One match record in full: engines, status, outcome, seed, time control,
/// start/end instants, forfeit info — and the replay / audit / .MAT
/// affordances once the record is terminal. Polls while the match is
/// running and pauses once the record is terminal (or the id is unknown);
/// navigating to a different id resets and resumes.
/// </summary>
public partial class MatchDetail
{
    private string? _watchedId;

    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>The match id from the route.</summary>
    [Parameter]
    public string MatchId { get; set; } = string.Empty;

    /// <summary>The latest record; null until the first load answers.</summary>
    protected MatchSummary? Match { get; private set; }

    /// <summary>True when the server answered 404 — no match with this id.</summary>
    protected bool NotFound { get; private set; }

    /// <inheritdoc />
    protected override bool ShouldPoll =>
        !NotFound && Match is null or { Status: MatchStatus.Running };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Same component instance, new id (in-app navigation): reset so the
        // paused poll loop picks the new id up on its next tick. The first
        // call after OnInitializedAsync must not wipe the initial load.
        if (_watchedId is not null && _watchedId != MatchId)
        {
            Match = null;
            NotFound = false;
        }
        _watchedId = MatchId;
    }

    /// <inheritdoc />
    protected override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        ArenaResult<MatchSummary> result = await Arena.GetMatchAsync(MatchId, cancellationToken);
        if (result.IsSuccess)
        {
            Match = result.Value;
            NotFound = false;
        }
        else
        {
            NotFound = true;
        }
    }
}
