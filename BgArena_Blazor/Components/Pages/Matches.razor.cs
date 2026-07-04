using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// The match dashboard: a launch form over the connected engines plus every
/// match record, newest first, polling-refreshed. Validation is
/// server-authoritative — the form submits and renders the server's typed
/// refusal reason rather than re-encoding the request rules client-side.
/// </summary>
public partial class Matches
{
    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>Navigation, for jumping to the launched match's detail page.</summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>Latest engine listing feeding the launch form; null until first load.</summary>
    protected IReadOnlyList<EngineSummary>? EngineRows { get; private set; }

    /// <summary>Latest match rows, newest first; null until first load.</summary>
    protected IReadOnlyList<MatchSummary>? MatchRows { get; private set; }

    /// <summary>The launch form's state.</summary>
    protected LaunchFormModel Form { get; } = new();

    /// <summary>The server's refusal reason for the last launch attempt, if any.</summary>
    protected string? LaunchError { get; private set; }

    /// <summary>Both engines picked and distinct — the only client-side gate.</summary>
    protected bool CanLaunch =>
        Form.EngineOne.Length > 0 && Form.EngineTwo.Length > 0 && Form.EngineOne != Form.EngineTwo;

    /// <inheritdoc />
    protected override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        EngineRows = await Arena.GetEnginesAsync(cancellationToken);
        // The server serves creation order; the dashboard reads newest first.
        MatchRows = [.. (await Arena.GetMatchesAsync(cancellationToken)).Reverse()];
    }

    /// <summary>Submits the launch form; navigates to the new match on success.</summary>
    protected async Task LaunchAsync()
    {
        ArenaResult<MatchSummary> result = await Arena.StartMatchAsync(
            new StartMatchRequest(Form.EngineOne, Form.EngineTwo, Form.MatchLength, Form.Seed, Form.MaxGames),
            PollCancellation);
        if (result.IsSuccess)
        {
            LaunchError = null;
            Navigation.NavigateTo($"matches/{result.Value.MatchId}");
        }
        else
        {
            LaunchError = result.Error ?? $"The server refused the launch ({(int)result.StatusCode}).";
        }
    }

    /// <summary>Mutable backing state for the launch form's inputs.</summary>
    protected sealed class LaunchFormModel
    {
        /// <summary>Engine name for seat One; empty until picked.</summary>
        public string EngineOne { get; set; } = string.Empty;

        /// <summary>Engine name for seat Two; empty until picked.</summary>
        public string EngineTwo { get; set; } = string.Empty;

        /// <summary>Match length in points; 0 starts a money session.</summary>
        public int MatchLength { get; set; } = 7;

        /// <summary>Optional dice seed; server-chosen and recorded when omitted.</summary>
        public int? Seed { get; set; }

        /// <summary>Optional games cap; required by the server for a money session.</summary>
        public int? MaxGames { get; set; }
    }
}
