using BackgammonDiagram_Lib;
using BgArena_Blazor.Components.Shared;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// Follow-live spectating of one match: subscribes to the per-move feed and
/// always renders the latest position — no stepping (replay owns that). It is
/// push, not poll, so it is a bespoke component rather than a
/// <see cref="PollingComponentBase"/>, but it mirrors that base's self-heal
/// semantics: a transport drop keeps the last board, raises the unreachable
/// banner, and re-subscribes (a fresh snapshot re-establishes state — the v1
/// feed has no <c>Last-Event-ID</c> resume); a contract break (a malformed
/// event) escalates into the circuit rather than being swallowed.
///
/// <para>The match summary is fetched once for the frame-free match context
/// (engine names, length) the board needs; the feed then supplies the game in
/// view, its entering score, its Crawford flag, and each position. A snapshot
/// establishes the join-in-progress state, increments follow, and the terminal
/// event shows the outcome and hands off to the replay page.</para>
/// </summary>
public partial class LiveMatch : IAsyncDisposable
{
    private readonly CancellationTokenSource _disposal = new();
    private string? _watchedId;
    private CancellationTokenSource? _watchCancellation;
    private Task? _watchLoop;
    private bool _isCrawford;

    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>The match id from the route.</summary>
    [Parameter]
    public string MatchId { get; set; } = string.Empty;

    /// <summary>Match-level context (engine names, length); null until the first summary load answers.</summary>
    protected MatchSummary? Summary { get; private set; }

    /// <summary>True when the server has no match with this id (404 on the summary or the subscribe).</summary>
    protected bool NotFound { get; private set; }

    /// <summary>True while the summary/feed is unreachable; a banner shows over the last board and the loop re-subscribes.</summary>
    protected bool IsUnreachable { get; private set; }

    /// <summary>1-based number of the game currently in view.</summary>
    protected int GameNumber { get; private set; }

    /// <summary>Seat One's match score entering the game in view.</summary>
    protected int SeatOneScore { get; private set; }

    /// <summary>Seat Two's match score entering the game in view.</summary>
    protected int SeatTwoScore { get; private set; }

    /// <summary>The latest board; null between games and before the opening roll.</summary>
    protected DiagramRequest? CurrentRequest { get; private set; }

    /// <summary>The caption for the latest action, or the between-games note.</summary>
    protected string? Caption { get; private set; }

    /// <summary>Why the latest position cannot be rendered, when it cannot.</summary>
    protected string? MappingError { get; private set; }

    /// <summary>The terminal record once the match ends — drives the outcome card and replay hand-off.</summary>
    protected MatchSummary? Terminal { get; private set; }

    /// <summary>Backoff between reconnect attempts, mirroring the poll cadence.</summary>
    protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(2);

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (_watchedId == MatchId)
            return;
        _watchedId = MatchId;

        // In-app navigation to a different id reuses this instance: end the
        // previous watch and reset before starting a fresh one for the new id.
        if (_watchCancellation is not null)
        {
            await _watchCancellation.CancelAsync();
            if (_watchLoop is not null)
            {
                try { await _watchLoop; }
                catch (OperationCanceledException) { /* expected on cancel */ }
            }
            _watchCancellation.Dispose();
        }

        ResetState();
        _watchCancellation = CancellationTokenSource.CreateLinkedTokenSource(_disposal.Token);
        _watchLoop = WatchAsync(MatchId, _watchCancellation.Token);
    }

    /// <summary>
    /// The watch loop: establish the match context, follow the feed to its
    /// terminal event, and re-subscribe across transport drops. Mirrors the
    /// poll loop's failure taxonomy — transport failures self-heal, a contract
    /// break propagates into the circuit.
    /// </summary>
    private async Task WatchAsync(string matchId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                ArenaResult<MatchSummary> summary = await Arena.GetMatchAsync(matchId, cancellationToken);
                if (!summary.IsSuccess)
                {
                    await ApplyAsync(() => NotFound = true);
                    return;
                }
                await ApplyAsync(() =>
                {
                    Summary = summary.Value;
                    NotFound = false;
                    IsUnreachable = false;
                });

                ArenaResult<IAsyncEnumerable<LiveMatchEvent>> subscription =
                    await Arena.SubscribeMatchLiveAsync(matchId, cancellationToken);
                if (!subscription.IsSuccess)
                {
                    // A documented 404 at subscribe time (the match was evicted
                    // between the summary and the subscribe) — the same terminal
                    // outcome as a summary 404.
                    await ApplyAsync(() => NotFound = true);
                    return;
                }

                await foreach (LiveMatchEvent liveEvent in subscription.Value.WithCancellation(cancellationToken))
                {
                    await ApplyAsync(() =>
                    {
                        IsUnreachable = false;
                        Apply(liveEvent);
                    });
                    if (liveEvent is LiveTerminalEvent)
                        return; // The terminal event is last; the server closes the stream.
                }
                // The stream ended without a terminal (server closed early):
                // fall through to reconnect.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return; // Disposal or an id change.
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                await ApplyAsync(() => IsUnreachable = true);
            }
            // A JsonException (a producer contract break) is deliberately not
            // caught here — it propagates out of the loop into the circuit.

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Folds one live event into the view state (runs on the dispatcher).</summary>
    private void Apply(LiveMatchEvent liveEvent)
    {
        switch (liveEvent)
        {
            case LiveSnapshotEvent snapshot:
                EnterGame(snapshot.GameNumber, snapshot.SeatOneScore, snapshot.SeatTwoScore, snapshot.IsCrawford);
                if (snapshot.Entries.Count > 0)
                    ShowEntry(snapshot.Entries[^1]);
                else
                    ShowWaiting();
                break;
            case LiveGameStartedEvent started:
                EnterGame(started.GameNumber, started.SeatOneScore, started.SeatTwoScore, started.IsCrawford);
                ShowWaiting();
                break;
            case LiveEntryEvent entry:
                ShowEntry(entry.Entry);
                break;
            case LiveGameEndedEvent ended:
                // The game in view finished; the next game-started (or the
                // terminal) event follows and supplies the next board.
                CurrentRequest = null;
                MappingError = null;
                Caption = $"Game {ended.Game.GameNumber} finished — awaiting the next game.";
                break;
            case LiveTerminalEvent terminal:
                Terminal = terminal.Match;
                Summary = terminal.Match;
                break;
        }
    }

    private void EnterGame(int gameNumber, int seatOneScore, int seatTwoScore, bool isCrawford)
    {
        GameNumber = gameNumber;
        SeatOneScore = seatOneScore;
        SeatTwoScore = seatTwoScore;
        _isCrawford = isCrawford;
    }

    private void ShowEntry(GameEntry entry)
    {
        DiagramContext context = DiagramContext.ForLiveGame(Summary!, SeatOneScore, SeatTwoScore, _isCrawford);
        try
        {
            CurrentRequest = ReplayDiagramMapper.ForEntry(context, entry);
            MappingError = null;
        }
        catch (InvalidOperationException exception)
        {
            // The diagram refused the position (e.g. a cube beyond its 4096 cap,
            // which the producer does not cap): fail visible, don't crash.
            CurrentRequest = null;
            MappingError = exception.Message;
        }
        Caption = ReplayNarration.Entry(context, entry);
    }

    private void ShowWaiting()
    {
        CurrentRequest = null;
        MappingError = null;
        Caption = _isCrawford
            ? $"Game {GameNumber} (Crawford) starting — waiting for the opening roll…"
            : $"Game {GameNumber} starting — waiting for the opening roll…";
    }

    private void ResetState()
    {
        Summary = null;
        NotFound = false;
        IsUnreachable = false;
        GameNumber = 0;
        SeatOneScore = 0;
        SeatTwoScore = 0;
        _isCrawford = false;
        CurrentRequest = null;
        Caption = null;
        MappingError = null;
        Terminal = null;
    }

    private Task ApplyAsync(Action mutation) => InvokeAsync(() =>
    {
        mutation();
        StateHasChanged();
    });

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _disposal.CancelAsync();
        if (_watchLoop is not null)
        {
            try { await _watchLoop; }
            catch (OperationCanceledException) { /* handled inside the loop */ }
        }
        _watchCancellation?.Dispose();
        _disposal.Dispose();
        GC.SuppressFinalize(this);
    }
}
