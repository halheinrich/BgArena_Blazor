using System.Net;
using BgArena_Blazor.Components.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the replay page over the routed transport stub: the
/// golden payload renders the viewer end to end (page → viewer → mapper →
/// diagram), and the two documented refusals (404 unknown id, 409 with the
/// server's reason) render distinctly.
/// </summary>
public class MatchReplayPageTests : BunitContext
{
    private void UseHandler(RoutedJsonHandler handler) =>
        Services.AddSingleton(handler.ToClient());

    [Fact]
    public void GoldenReplay_RendersHeaderViewerAndBoard()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/games", CannedJson.GoldenReplay));

        var cut = Render<MatchReplay>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha vs Beta", cut.Find("h1").TextContent);
            Assert.Contains("3-point match", cut.Markup);
            Assert.Equal("Alpha rolls 3-1: 8/5 6/5", cut.Find("#step-caption").TextContent);
            Assert.NotNull(cut.Find(".replay-board svg"));
        });
    }

    [Fact]
    public void TerminalNonCompleted_RendersThePartialityNoteAboveTheViewer()
    {
        // A forfeited match now serves the games that finished before the break;
        // the page signals the partiality from the response's status, not by
        // re-deriving it.
        const string forfeitedReplay =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":5,"status":"forfeited","games":[]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/games", forfeitedReplay));

        var cut = Render<MatchReplay>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            string note = cut.Find("#replay-partial").TextContent;
            Assert.Contains("Forfeited", note);
            Assert.Contains("0 completed games", note);
        });
    }

    [Fact]
    public void InterruptedMatch_RendersThePartialityNoteFromTheServedStatus()
    {
        // An interrupted match (journal-rehydrated) serves the games that
        // finished before the server died; the page signals the partiality from
        // the response's terminal status, the same path as forfeited/aborted.
        const string interruptedReplay =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":5,"status":"interrupted","games":[]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/games", interruptedReplay));

        var cut = Render<MatchReplay>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            string note = cut.Find("#replay-partial").TextContent;
            Assert.Contains("Interrupted", note);
            Assert.Contains("0 completed games", note);
        });
    }

    [Fact]
    public void KnownButNotCompleted_RendersTheServersReasonWithRetry()
    {
        UseHandler(new RoutedJsonHandler().Map(
            "GET /matches/match-1/games",
            """{"error":"Match 'match-1' is not completed: running."}""",
            HttpStatusCode.Conflict));

        var cut = Render<MatchReplay>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Match 'match-1' is not completed: running.",
                cut.Find("#replay-refused").TextContent);
            Assert.NotNull(cut.Find("#retry-replay"));
        });
    }

    [Fact]
    public void UnknownId_RendersNotFound()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/nope/games", "", HttpStatusCode.NotFound));

        var cut = Render<MatchReplay>(p => p.Add(c => c.MatchId, "nope"));

        cut.WaitForAssertion(() => Assert.Contains("No match with id 'nope'", cut.Markup));
    }
}
