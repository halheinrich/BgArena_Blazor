extern alias TournamentServer;

using System.Net;
using System.Net.WebSockets;
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
            foreach (GameEntry entry in game.Entries)
                Assert.NotNull(ReplayDiagramMapper.ForEntry(replay, game, entry));
            Assert.NotNull(ReplayDiagramMapper.ForFinalState(replay, game));
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
}
