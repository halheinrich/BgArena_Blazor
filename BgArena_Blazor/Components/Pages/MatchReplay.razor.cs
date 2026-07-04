using System.Net;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// Step-through replay of a completed match. Loads once — a Completed
/// match's transcripts are immutable, so there is nothing to poll. The two
/// documented refusals render distinctly: 404 is an unknown id; 409 carries
/// the server's reason (running: not yet; forfeited/aborted/faulted: no
/// transcripts retained — the v1 contract) with a retry for the
/// might-complete-later case.
/// </summary>
public partial class MatchReplay : IDisposable
{
    private readonly CancellationTokenSource _disposal = new();
    private string? _loadedId;

    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>The match id from the route.</summary>
    [Parameter]
    public string MatchId { get; set; } = string.Empty;

    /// <summary>The replay payload; null until a load succeeds.</summary>
    protected MatchGamesResponse? Replay { get; private set; }

    /// <summary>True when the server answered 404 — no match with this id.</summary>
    protected bool NotFound { get; private set; }

    /// <summary>The server's 409 reason — the match is known but has no replay.</summary>
    protected string? RefusalReason { get; private set; }

    /// <summary>True when the load failed at the transport layer.</summary>
    protected bool Unreachable { get; private set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == MatchId)
            return;
        _loadedId = MatchId;
        await LoadAsync();
    }

    /// <summary>Loads (or retries) the replay for the current id.</summary>
    protected async Task LoadAsync()
    {
        Replay = null;
        NotFound = false;
        RefusalReason = null;
        Unreachable = false;
        try
        {
            ArenaResult<MatchGamesResponse> result = await Arena.GetMatchGamesAsync(MatchId, _disposal.Token);
            if (result.IsSuccess)
                Replay = result.Value;
            else if (result.StatusCode == HttpStatusCode.NotFound)
                NotFound = true;
            else
                RefusalReason = result.Error ?? $"The server refused the replay ({(int)result.StatusCode}).";
        }
        catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
        {
            // Disposal raced the load; nothing to render.
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Unreachable = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposal.Cancel();
        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }
}
