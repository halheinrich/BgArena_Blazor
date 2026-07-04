extern alias TournamentServer;

using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using BackgammonDiagram_Lib;
using BgArena_Blazor.Components.Shared;
using BgArena_Blazor.Services;
using BgTournament.Api;
using BgTournament.EngineClient;
using Bunit;
using Microsoft.AspNetCore.Mvc.Testing;
using Engine = BgTournament.EngineClient.EngineClient;

namespace BgArena_Blazor.Tests;

/// <summary>
/// The gating smoke — the primary user path end to end against the real
/// system, no stubs anywhere: the real tournament server boots in-proc
/// (WebApplicationFactory over BgTournament.Server's internal Program,
/// granted test-only), two reference engines connect over the real wire
/// (EngineClient.ServeAsync on TestServer sockets), ArenaClient starts a
/// fixed-seed match and polls it to completion, the replay endpoint's real
/// JSON is consumed, every entry and finalState of every game runs through
/// ReplayDiagramMapper (Builder-validated), and ReplayViewer renders and
/// steps through the whole payload. The canned-JSON tests elsewhere are
/// convenience fixtures; this is where the contract is proven.
/// </summary>
public class ArenaSmokeTests : BunitContext
{
    private static readonly TimeSpan CompletionDeadline = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RegistrationDeadline = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static string Json<T>(T value) => JsonSerializer.Serialize(value, WebJson);

    /// <summary>
    /// Connects a reference engine (random play, passive cube) the way any
    /// remote engine enters: a real WebSocket served by EngineClient. The
    /// connect is awaited so a failure surfaces here, not in a swallowed
    /// background task (the producer's documented wire-test pitfall).
    /// </summary>
    private static async Task ConnectReferenceEngineAsync(
        WebApplicationFactory<TournamentServer::Program> factory, string name, int seed)
    {
        WebSocket socket = await factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/engine"), CancellationToken.None);
        var engine = new Engine(
            new EngineIdentity(name), new RandomPlayAgent(seed), new PassiveCubeAgent());
        _ = Task.Run(async () =>
        {
            try
            {
                await engine.ServeAsync(socket);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
            {
                // Test teardown: the server or socket ended the session.
            }
        });
    }

    private static async Task WaitForEnginesAsync(ArenaClient arena, params string[] names)
    {
        DateTime deadline = DateTime.UtcNow + RegistrationDeadline;
        while (DateTime.UtcNow < deadline)
        {
            IReadOnlyList<EngineSummary> engines = await arena.GetEnginesAsync();
            if (names.All(name => engines.Any(engine => engine.Name == name)))
                return;
            await Task.Delay(25);
        }
        Assert.Fail($"Engines [{string.Join(", ", names)}] did not register within {RegistrationDeadline}.");
    }

    private static async Task<MatchSummary> WaitForCompletionAsync(ArenaClient arena, string matchId)
    {
        DateTime deadline = DateTime.UtcNow + CompletionDeadline;
        while (DateTime.UtcNow < deadline)
        {
            ArenaResult<MatchSummary> result = await arena.GetMatchAsync(matchId);
            Assert.True(result.IsSuccess, $"Match '{matchId}' vanished mid-run.");
            if (result.Value.Status != MatchStatus.Running)
                return result.Value;
            await Task.Delay(100);
        }
        Assert.Fail($"Match '{matchId}' did not finish within {CompletionDeadline}.");
        throw new UnreachableException();
    }

    [Fact]
    public async Task PrimaryPath_WireMatchToCompletion_ReplayConsumedMappedAndRendered()
    {
        using var factory = new WebApplicationFactory<TournamentServer::Program>();
        var arena = new ArenaClient(factory.CreateClient());

        // Two reference engines over the real wire; fully deterministic seeds.
        await ConnectReferenceEngineAsync(factory, "SmokeAlpha", seed: 11);
        await ConnectReferenceEngineAsync(factory, "SmokeBeta", seed: 22);
        await WaitForEnginesAsync(arena, "SmokeAlpha", "SmokeBeta");

        // Launch through the typed client — the same path the launch form uses.
        ArenaResult<MatchSummary> started = await arena.StartMatchAsync(
            new StartMatchRequest("SmokeAlpha", "SmokeBeta", MatchLength: 2, Seed: 4242));
        Assert.True(started.IsSuccess, started.IsSuccess ? null : started.Error);
        string matchId = started.Value.MatchId;

        // Poll to completion — the dashboard's read path against live records.
        MatchSummary finished = await WaitForCompletionAsync(arena, matchId);
        Assert.Equal(MatchStatus.Completed, finished.Status);
        Assert.NotNull(finished.Winner);
        Assert.NotNull(finished.SeatOneScore);
        Assert.NotNull(finished.SeatTwoScore);

        // The replay contract against the server's real JSON.
        ArenaResult<MatchGamesResponse> games = await arena.GetMatchGamesAsync(matchId);
        Assert.True(games.IsSuccess, games.IsSuccess ? null : games.Error);
        MatchGamesResponse replay = games.Value;
        Assert.Equal(("SmokeAlpha", "SmokeBeta", 2), (replay.EngineOne, replay.EngineTwo, replay.MatchLength));
        Assert.NotEmpty(replay.Games);

        // Every renderable position of every game must survive the mapper
        // (Builder validation runs on each).
        foreach (GameReplay game in replay.Games)
        {
            Assert.NotEmpty(game.Entries);
            DiagramContext context = DiagramContext.ForGame(replay, game);
            foreach (GameEntry entry in game.Entries)
                Assert.NotNull(ReplayDiagramMapper.ForEntry(context, entry));
            Assert.NotNull(ReplayDiagramMapper.ForFinalState(context, game.FinalState));
        }

        // And the viewer renders + steps through the whole real payload.
        var cut = Render<ReplayViewer>(p => p.Add(c => c.Replay, replay));
        for (int gameIndex = 0; gameIndex < replay.Games.Count; gameIndex++)
        {
            cut.Find("#game-pick").Change(gameIndex.ToString());
            int entryCount = replay.Games[gameIndex].Entries.Count;
            for (int step = 0; step < entryCount; step++)
            {
                Assert.NotNull(cut.Find(".replay-board svg"));
                Assert.NotEqual(string.Empty, cut.Find("#step-caption").TextContent.Trim());
                cut.Find("#step-next").Click();
            }
            // The step past the last entry is the game's final state.
            Assert.NotNull(cut.Find(".replay-board svg"));
            Assert.Contains("over —", cut.Find("#step-caption").TextContent);
            Assert.True(cut.Find("#step-next").HasAttribute("disabled"));
        }
    }

    /// <summary>
    /// Connects a reference engine whose play agent is gated shut, so the match
    /// parks at its first play query until the gate is released. This makes the
    /// live subscription deterministic: the subscriber joins while the match is
    /// parked, so its snapshot arrives first and no increment is missed.
    /// </summary>
    private static async Task<GatedPlayAgent> ConnectGatedEngineAsync(
        WebApplicationFactory<TournamentServer::Program> factory, string name, int seed)
    {
        var gate = new GatedPlayAgent(new RandomPlayAgent(seed));
        WebSocket socket = await factory.Server.CreateWebSocketClient()
            .ConnectAsync(new Uri("ws://localhost/engine"), CancellationToken.None);
        var engine = new Engine(new EngineIdentity(name), gate, new PassiveCubeAgent());
        _ = Task.Run(async () =>
        {
            try
            {
                await engine.ServeAsync(socket);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException)
            {
                // Test teardown: the server or socket ended the session.
            }
        });
        return gate;
    }

    [Fact]
    public async Task LivePath_WireMatch_SnapshotFirstTerminalLast_GamesAgreeWithReplay_AndBoardsRender()
    {
        using var factory = new WebApplicationFactory<TournamentServer::Program>();
        var arena = new ArenaClient(factory.CreateClient());
        using var deadline = new CancellationTokenSource(CompletionDeadline);

        // Gated engines: parked until released, so the subscriber catches the
        // whole feed from the top — the strongest form of the agreement check.
        GatedPlayAgent alpha = await ConnectGatedEngineAsync(factory, "LiveAlpha", seed: 11);
        GatedPlayAgent beta = await ConnectGatedEngineAsync(factory, "LiveBeta", seed: 22);
        await WaitForEnginesAsync(arena, "LiveAlpha", "LiveBeta");

        ArenaResult<MatchSummary> started = await arena.StartMatchAsync(
            new StartMatchRequest("LiveAlpha", "LiveBeta", MatchLength: 3, Seed: 4242));
        Assert.True(started.IsSuccess, started.IsSuccess ? null : started.Error);
        string matchId = started.Value.MatchId;

        // Subscribe through the typed client — the same path the live page uses.
        ArenaResult<IAsyncEnumerable<LiveMatchEvent>> subscription =
            await arena.SubscribeMatchLiveAsync(matchId, deadline.Token);
        Assert.True(subscription.IsSuccess);

        // Collect on a background task. The snapshot is sent on subscribe, before
        // any increment (the match is parked), so signalling on the first event
        // lets us release the gates only once the subscription is established.
        var events = new List<LiveMatchEvent>();
        var snapshotSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task collector = Task.Run(async () =>
        {
            await foreach (LiveMatchEvent liveEvent in subscription.Value.WithCancellation(deadline.Token))
            {
                events.Add(liveEvent);
                snapshotSeen.TrySetResult();
                if (liveEvent is LiveTerminalEvent)
                    return;
            }
        });

        await snapshotSeen.Task.WaitAsync(deadline.Token);
        alpha.Release();
        beta.Release();
        await collector.WaitAsync(deadline.Token);

        // Ordering: snapshot first, exactly one terminal, and it is last.
        Assert.IsType<LiveSnapshotEvent>(events[0]);
        LiveTerminalEvent terminal = Assert.IsType<LiveTerminalEvent>(events[^1]);
        Assert.Equal(MatchStatus.Completed, terminal.Match.Status);
        Assert.All(events.SkipLast(1), e => Assert.IsNotType<LiveTerminalEvent>(e));

        // Agreement: the games finalized live equal exactly what the settled
        // replay serves — same count, each JSON-identical.
        ArenaResult<MatchGamesResponse> replayResult = await arena.GetMatchGamesAsync(matchId, deadline.Token);
        Assert.True(replayResult.IsSuccess, replayResult.IsSuccess ? null : replayResult.Error);
        MatchGamesResponse replay = replayResult.Value;

        List<GameReplay> endedLive = [.. events.OfType<LiveGameEndedEvent>().Select(e => e.Game)];
        Assert.Equal(replay.Games.Count, endedLive.Count);
        foreach ((GameReplay served, GameReplay live) in replay.Games.Zip(endedLive))
            Assert.Equal(Json(served), Json(live));

        // Every live board state renders through the page's mapping path
        // (DiagramContext.ForLiveGame → ReplayDiagramMapper → ReplayBoard).
        int boardsRendered = RenderLiveBoards(terminal.Match, events);
        Assert.True(boardsRendered > 0, "Expected at least one live board to render.");
    }

    /// <summary>
    /// Walks the collected feed exactly as the live page does — tracking the
    /// game-in-view context off the snapshot / game-started events — and renders
    /// every board-bearing state through <see cref="ReplayBoard"/>, asserting
    /// each draws. Returns the number of boards rendered.
    /// </summary>
    private int RenderLiveBoards(MatchSummary summary, IEnumerable<LiveMatchEvent> events)
    {
        int seatOneScore = 0, seatTwoScore = 0, rendered = 0;
        bool isCrawford = false;
        foreach (LiveMatchEvent liveEvent in events)
        {
            switch (liveEvent)
            {
                case LiveSnapshotEvent snapshot:
                    (seatOneScore, seatTwoScore, isCrawford) =
                        (snapshot.SeatOneScore, snapshot.SeatTwoScore, snapshot.IsCrawford);
                    if (snapshot.Entries.Count > 0)
                        rendered += RenderLiveEntry(summary, seatOneScore, seatTwoScore, isCrawford, snapshot.Entries[^1]);
                    break;
                case LiveGameStartedEvent started:
                    (seatOneScore, seatTwoScore, isCrawford) =
                        (started.SeatOneScore, started.SeatTwoScore, started.IsCrawford);
                    break;
                case LiveEntryEvent entry:
                    rendered += RenderLiveEntry(summary, seatOneScore, seatTwoScore, isCrawford, entry.Entry);
                    break;
            }
        }
        return rendered;
    }

    private int RenderLiveEntry(
        MatchSummary summary, int seatOneScore, int seatTwoScore, bool isCrawford, GameEntry entry)
    {
        DiagramContext context = DiagramContext.ForLiveGame(summary, seatOneScore, seatTwoScore, isCrawford);
        DiagramRequest request = ReplayDiagramMapper.ForEntry(context, entry);
        IRenderedComponent<ReplayBoard> cut = Render<ReplayBoard>(p => p.Add(c => c.Request, request));
        Assert.NotNull(cut.Find(".replay-board svg"));
        return 1;
    }

    [Fact]
    public async Task RefusalPath_UnknownEngine_SurfacesTheTypedReasonFromTheRealServer()
    {
        using var factory = new WebApplicationFactory<TournamentServer::Program>();
        var arena = new ArenaClient(factory.CreateClient());

        ArenaResult<MatchSummary> result = await arena.StartMatchAsync(
            new StartMatchRequest("Ghost", "AlsoGhost", MatchLength: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private sealed class UnreachableException : Exception;

    /// <summary>
    /// A play agent that parks its first decision behind a gate, then delegates
    /// every choice (including that first one, once released) to an inner agent.
    /// Wrapping a seeded <see cref="RandomPlayAgent"/> keeps the match fully
    /// deterministic — the gate only delays the first query, it never changes a
    /// choice — while letting the test register its live subscription before any
    /// move is recorded.
    /// </summary>
    private sealed class GatedPlayAgent(BgGame_Lib.IPlayAgent inner) : BgGame_Lib.IPlayAgent
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Opens the gate; the parked (and all subsequent) queries proceed.</summary>
        public void Release() => _gate.TrySetResult();

        /// <inheritdoc />
        public async ValueTask<BgDataTypes_Lib.Play> ChoosePlayAsync(
            BgGame_Lib.GameState state, int die1, int die2, CancellationToken cancellationToken = default)
        {
            await _gate.Task.WaitAsync(cancellationToken);
            return await inner.ChoosePlayAsync(state, die1, die2, cancellationToken);
        }
    }
}
