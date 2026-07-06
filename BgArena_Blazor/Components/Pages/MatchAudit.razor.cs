using System.Net;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// A terminal match's arbitration timeline, rendered as a verbatim flat list
/// of type-styled rows — the audit surface's viewer, sibling to the replay
/// page. Loads once — a terminal match's audit is immutable, so there is
/// nothing to poll. Refusals mirror the replay page: 404 is an unknown id (or
/// a known record whose audit journal cannot be read — that flavor carries
/// the server's reason); 409 means the match is still running, so the page
/// offers the live view alongside a retry.
/// </summary>
public partial class MatchAudit : IDisposable
{
    private readonly CancellationTokenSource _disposal = new();
    private string? _loadedId;

    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>The match id from the route.</summary>
    [Parameter]
    public string MatchId { get; set; } = string.Empty;

    /// <summary>The audit payload; null until a load succeeds.</summary>
    protected MatchAuditResponse? Audit { get; private set; }

    /// <summary>True when the server answered 404.</summary>
    protected bool NotFound { get; private set; }

    /// <summary>
    /// The 404's reason when it carried one (the record exists but its audit
    /// journal cannot be read); null for the plain unknown-id flavor.
    /// </summary>
    protected string? NotFoundReason { get; private set; }

    /// <summary>The server's 409 reason — the match is known but still running.</summary>
    protected string? RefusalReason { get; private set; }

    /// <summary>True when the load failed at the transport layer.</summary>
    protected bool Unreachable { get; private set; }

    /// <summary>
    /// Whether the match ran under a Fischer clock (the created event's
    /// recorded configuration) — gates the clock-correlation legend. A
    /// clockless (flat-regime) timeline simply has no clock rows; that is
    /// normal, not a gap, so the legend would be noise there.
    /// </summary>
    protected bool IsClocked =>
        Audit?.Events.OfType<AuditCreatedEvent>().FirstOrDefault()?.TimeControl is not null;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_loadedId == MatchId)
            return;
        _loadedId = MatchId;
        await LoadAsync();
    }

    /// <summary>Loads (or retries) the audit timeline for the current id.</summary>
    protected async Task LoadAsync()
    {
        Audit = null;
        NotFound = false;
        NotFoundReason = null;
        RefusalReason = null;
        Unreachable = false;
        try
        {
            ArenaResult<MatchAuditResponse> result = await Arena.GetMatchAuditAsync(MatchId, _disposal.Token);
            if (result.IsSuccess)
            {
                Audit = result.Value;
            }
            else if (result.StatusCode == HttpStatusCode.NotFound)
            {
                NotFound = true;
                NotFoundReason = result.Error;
            }
            else
            {
                RefusalReason = result.Error ?? $"The server refused the audit ({(int)result.StatusCode}).";
            }
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
