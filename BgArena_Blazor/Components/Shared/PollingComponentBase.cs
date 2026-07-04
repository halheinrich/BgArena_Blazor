using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// The one polling implementation behind every auto-refreshing page: one
/// initial load in <see cref="OnInitializedAsync"/>, then a
/// <see cref="PeriodicTimer"/> loop that re-runs <see cref="RefreshAsync"/>
/// on the dispatcher and re-renders. A failed poll keeps the last good data
/// and raises <see cref="IsUnreachable"/>; polling continues, so the page
/// self-heals when the server comes back. Ticks where <see cref="ShouldPoll"/>
/// is false skip the refresh without ending the loop — a detail page paused
/// on a terminal record resumes by itself if its parameters change. Anything
/// other than a transport failure is dispatched into the component's
/// lifecycle (fail loud), never swallowed by the background loop.
/// </summary>
public abstract class PollingComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _disposal = new();
    private PeriodicTimer? _timer;
    private Task? _pollLoop;

    /// <summary>Poll cadence. 2 s everywhere by default — the v1 dashboard call.</summary>
    protected virtual TimeSpan PollInterval => TimeSpan.FromSeconds(2);

    /// <summary>
    /// Whether the next tick should refresh. Override to pause on terminal
    /// state (a completed match, an unknown id); the loop keeps ticking and
    /// re-evaluates, so flipping back true resumes polling.
    /// </summary>
    protected virtual bool ShouldPoll => true;

    /// <summary>
    /// True when the latest refresh failed at the transport layer; the page
    /// shows a banner over the last good data while the loop keeps retrying.
    /// </summary>
    protected bool IsUnreachable { get; private set; }

    /// <summary>Cancelled when the component is disposed; pass to every arena call.</summary>
    protected CancellationToken PollCancellation => _disposal.Token;

    /// <summary>Loads or reloads the page's data — once at init, then per tick.</summary>
    protected abstract Task RefreshAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await GuardedRefreshAsync();
        _pollLoop = PollLoopAsync();
    }

    private async Task PollLoopAsync()
    {
        _timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await _timer.WaitForNextTickAsync(_disposal.Token))
            {
                if (!ShouldPoll)
                    continue;
                await InvokeAsync(async () =>
                {
                    await GuardedRefreshAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Disposal — normal shutdown.
        }
        catch (Exception exception)
        {
            // A background-loop failure must not vanish: route it into the
            // component lifecycle so the circuit surfaces it.
            await DispatchExceptionAsync(exception);
        }
    }

    private async Task GuardedRefreshAsync()
    {
        try
        {
            await RefreshAsync(_disposal.Token);
            IsUnreachable = false;
        }
        catch (OperationCanceledException) when (_disposal.IsCancellationRequested)
        {
            // Disposal raced a refresh mid-flight; the loop is ending anyway.
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Server unreachable, errored, or timed out: keep the last good
            // data, flag the banner, let the next tick retry. Anything else
            // (e.g. a JsonException — a contract break) propagates.
            IsUnreachable = true;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _disposal.CancelAsync();
        _timer?.Dispose();
        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop;
            }
            catch (OperationCanceledException)
            {
                // Already handled inside the loop; nothing to observe.
            }
        }
        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }
}
