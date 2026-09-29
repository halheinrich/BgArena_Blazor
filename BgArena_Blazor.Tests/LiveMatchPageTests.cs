using System.Text.Json;
using BgArena_Blazor.Components.Pages;
using BgTournament.Api;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the live spectating page over the routed transport
/// stub: the match summary loads for context, then the SSE feed drives the
/// board through the page's real mapping path. Covers the join-in-progress
/// snapshot, a between-games (game-started) placeholder — including the
/// Crawford flag now carried on the feed — what the live board states (the
/// feed's Crawford flag, no decision-domain text on a cube entry, the money
/// label with no Jacoby rule), the terminal hand-off to replay, and the two
/// failure surfaces (unknown id, unreachable server).
/// </summary>
public class LiveMatchPageTests : BunitContext
{
    private static readonly int[] OpeningBoard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static GamePosition Pos() => new(OpeningBoard, 1, CubeOwner.Centered);

    private static PlayEntry OpeningPlay() =>
        new(Seat.One, Pos(), Die1: 3, Die2: 1, Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]);

    /// <summary>A completed final record, as the terminal event carries it.</summary>
    private static MatchSummary CompletedSummary() =>
        new("match-1", "Alpha", "Beta", MatchLength: 7, MaxGames: null, Seed: 42,
            TimeControl: null, MatchStatus.Completed, Winner: "Alpha", SeatOneScore: 7, SeatTwoScore: 3,
            ForfeitedBy: null, ForfeitCause: null, Detail: null, StartedAtUtc: default, EndedAtUtc: null);

    /// <summary>An interrupted final record — a terminal status whose end time
    /// died with the server (endedAtUtc null), reconstructed from the journal.</summary>
    private static MatchSummary InterruptedSummary() =>
        new("match-1", "Alpha", "Beta", MatchLength: 7, MaxGames: null, Seed: 42,
            TimeControl: null, MatchStatus.Interrupted, Winner: null, SeatOneScore: null, SeatTwoScore: null,
            ForfeitedBy: null, ForfeitCause: null,
            Detail: "The server was interrupted while this match was running; the record was reconstructed from its journal.",
            StartedAtUtc: default, EndedAtUtc: null);

    /// <summary>Wraps events as an SSE body — one <c>data:</c> frame per event.</summary>
    private static string Sse(params LiveMatchEvent[] events) =>
        string.Concat(events.Select(e =>
            $"data: {JsonSerializer.Serialize(e, JsonSerializerOptions.Web)}\n\n"));

    private void UseHandler(RoutedJsonHandler handler) =>
        Services.AddSingleton(handler.ToClient());

    private IRenderedComponent<LiveMatch> RenderLive() =>
        Render<LiveMatch>(p => p.Add(c => c.MatchId, "match-1"));

    /// <summary>Every text the drawn board's SVG shows, in document order.</summary>
    private static IReadOnlyList<string> BoardTexts(IRenderedComponent<LiveMatch> cut) =>
        [.. cut.FindAll(".replay-board text").Select(text => text.TextContent)];

    [Fact]
    public void Snapshot_RendersScoreboardCaptionAndBoard()
    {
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(new LiveSnapshotEvent(GameNumber: 1, SeatOneScore: 0, SeatTwoScore: 0,
                    IsCrawford: false, Entries: [OpeningPlay()]))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha vs Beta", cut.Find("h1").TextContent);
            Assert.Contains("Game 1", cut.Find("#live-scoreboard").TextContent);
            Assert.Equal("Alpha rolls 3-1: 8/5 6/5", cut.Find("#live-caption").TextContent);
            Assert.NotNull(cut.Find(".replay-board svg"));
        });
    }

    [Fact]
    public void EmptySnapshot_WaitsForTheOpeningRoll()
    {
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: []))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("#live-placeholder"));
            Assert.Empty(cut.FindAll(".replay-board"));
            Assert.Contains("waiting for the opening roll", cut.Find("#live-caption").TextContent);
        });
    }

    [Fact]
    public void GameStarted_ShowsTheBetweenGamesPlaceholderCarryingCrawford()
    {
        // The Crawford flag now rides the feed (game-start context), so the
        // between-games note names it — no client-side derivation, no default lie.
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(
                    new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: [OpeningPlay()]),
                    new LiveGameStartedEvent(GameNumber: 2, SeatOneScore: 6, SeatTwoScore: 0, IsCrawford: true))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("#live-placeholder"));
            Assert.Empty(cut.FindAll(".replay-board"));
            Assert.Contains("Game 2 (Crawford)", cut.Find("#live-caption").TextContent);
        });
    }

    [Fact]
    public void CrawfordGame_DrawsTheFeedsFlagOnTheLiveBoard()
    {
        // The live board states the Crawford flag the feed carries, drawn by
        // the diagram on the rails and the cube.
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(new LiveSnapshotEvent(GameNumber: 7, SeatOneScore: 6, SeatTwoScore: 3,
                    IsCrawford: true, Entries: [OpeningPlay()]))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            IReadOnlyList<string> texts = BoardTexts(cut);
            Assert.Contains("Alpha needs 1 Crawford", texts);
            Assert.Contains("Beta needs 4 Crawford", texts);
            Assert.Contains("Cr", texts);
        });
    }

    [Fact]
    public void CubeEntry_DrawsNoDecisionText()
    {
        // A cube offer followed live is a board, never a decision: no
        // "Cube Action?" and no roll.
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(
                    new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: [OpeningPlay()]),
                    new LiveEntryEvent(new CubeOfferEntry(Seat.Two, Pos())))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("Beta doubles to 2", cut.Find("#live-caption").TextContent);
            IReadOnlyList<string> texts = BoardTexts(cut);
            Assert.Contains("Alpha needs 7", texts);
            Assert.DoesNotContain("Cube Action?", texts);
            Assert.DoesNotContain(texts, text => text.Contains("to play", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void MoneySession_DrawsTheMoneyLabelWithNoJacobyRule()
    {
        // The API states no Jacoby rule, so the live board states none.
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMoneySession)
            .MapEventStream("GET /matches/match-1/live",
                Sse(new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: [OpeningPlay()]))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            IReadOnlyList<string> texts = BoardTexts(cut);
            Assert.Contains("Alpha (Money Game)", texts);
            Assert.Contains("Beta (Money Game)", texts);
            Assert.DoesNotContain(texts, text => text.Contains("Jacoby", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Terminal_ShowsTheOutcomeAndHandsOffToReplay()
    {
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.RunningMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(
                    new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: [OpeningPlay()]),
                    new LiveTerminalEvent(CompletedSummary()))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            Assert.NotNull(cut.Find("#live-terminal"));
            Assert.Contains("Completed", cut.Find("#live-terminal").TextContent);
            Assert.Contains("7–3", cut.Find("#live-terminal").TextContent);
            Assert.Equal("matches/match-1/replay", cut.Find("#live-replay-link").GetAttribute("href"));
            // The live board gives way to the outcome card.
            Assert.Empty(cut.FindAll("#live-scoreboard"));
        });
    }

    [Fact]
    public void InterruptedTerminal_ShowsTheOutcomeAndHandsOffToReplay()
    {
        // Joining an interrupted match (only ever rehydrated) yields the journal
        // snapshot then the terminal event carrying the interrupted record: the
        // page shows the outcome card — status pill and all — and the replay
        // hand-off, exactly as for any other terminal outcome.
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", CannedJson.InterruptedMatch)
            .MapEventStream("GET /matches/match-1/live",
                Sse(
                    new LiveSnapshotEvent(1, 0, 0, IsCrawford: false, Entries: [OpeningPlay()]),
                    new LiveTerminalEvent(InterruptedSummary()))));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
        {
            var terminal = cut.Find("#live-terminal");
            Assert.Contains("Interrupted", terminal.TextContent);
            Assert.Contains("status-interrupted", terminal.QuerySelector("span.status")!.GetAttribute("class"));
            Assert.Equal("matches/match-1/replay", cut.Find("#live-replay-link").GetAttribute("href"));
            Assert.Empty(cut.FindAll("#live-scoreboard"));
        });
    }

    [Fact]
    public void UnknownId_RendersNotFound()
    {
        UseHandler(new RoutedJsonHandler()
            .Map("GET /matches/match-1", "", System.Net.HttpStatusCode.NotFound));

        var cut = RenderLive();

        cut.WaitForAssertion(() => Assert.Contains("No match with id 'match-1'", cut.Markup));
    }

    [Fact]
    public void UnreachableServer_RaisesTheBanner()
    {
        UseHandler(new RoutedJsonHandler { ThrowConnectionError = true }
            .Map("GET /matches/match-1", CannedJson.RunningMatch));

        var cut = RenderLive();

        cut.WaitForAssertion(() =>
            Assert.Contains("Tournament server unreachable", cut.Markup));
    }
}
