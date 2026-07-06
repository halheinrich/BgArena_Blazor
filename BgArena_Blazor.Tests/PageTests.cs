using System.Net;
using BgArena_Blazor.Components.Pages;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the dashboard pages over a routed transport stub:
/// initial load rendering, the unreachable banner, form submission through
/// the real ArenaClient (parent → child → handler), server refusals rendered
/// from the typed error, and detail-page terminal states.
/// </summary>
public class PageTests : BunitContext
{
    private RoutedJsonHandler UseHandler(RoutedJsonHandler handler)
    {
        Services.AddSingleton(handler.ToClient());
        return handler;
    }

    // ---- Engines ------------------------------------------------------------

    [Fact]
    public void EnginesPage_ListsEnginesWithTheirClaimState()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /engines", CannedJson.MixedEngines));

        var cut = Render<Engines>();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll("tbody tr");
            Assert.Equal(2, rows.Count);
            Assert.Contains("idle", rows[0].TextContent);
            Assert.Contains("in match", rows[1].TextContent);
        });
    }

    [Fact]
    public void EnginesPage_UnreachableServer_ShowsTheBanner()
    {
        UseHandler(new RoutedJsonHandler { ThrowConnectionError = true });

        var cut = Render<Engines>();

        cut.WaitForAssertion(() => Assert.Contains("unreachable", cut.Markup));
    }

    // ---- Matches ------------------------------------------------------------

    private RoutedJsonHandler MatchesPageHandler() =>
        UseHandler(new RoutedJsonHandler()
            .Map("GET /engines", CannedJson.TwoIdleEngines)
            .Map("GET /matches", "[]"));

    [Fact]
    public void MatchesPage_LaunchPostsTheRequestAndNavigatesToTheMatch()
    {
        RoutedJsonHandler handler = MatchesPageHandler()
            .Map("POST /matches", CannedJson.RunningMatch);

        var cut = Render<Matches>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("#engine-one option").Count > 1));

        cut.Find("#engine-one").Change("Alpha");
        cut.Find("#engine-two").Change("Beta");
        cut.Find("#launch-button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(
                """{"engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"seed":null,"maxGames":null,"timeControl":null}""",
                handler.LastPostBody);
            var navigation = Services.GetRequiredService<NavigationManager>();
            Assert.EndsWith("matches/match-1", navigation.Uri);
        });
    }

    [Fact]
    public void MatchesPage_RefusedLaunch_RendersTheServersReason()
    {
        MatchesPageHandler()
            .Map("POST /matches", """{"error":"Engine 'Alpha' is already in a match."}""",
                HttpStatusCode.Conflict);

        var cut = Render<Matches>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("#engine-one option").Count > 1));

        cut.Find("#engine-one").Change("Alpha");
        cut.Find("#engine-two").Change("Beta");
        cut.Find("#launch-button").Click();

        cut.WaitForAssertion(() =>
            Assert.Contains("Engine 'Alpha' is already in a match.", cut.Find("#launch-error").TextContent));
    }

    [Fact]
    public void MatchesPage_LaunchButtonGatesOnDistinctEngines()
    {
        MatchesPageHandler();

        var cut = Render<Matches>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("#engine-one option").Count > 1));

        Assert.True(cut.Find("#launch-button").HasAttribute("disabled"));

        cut.Find("#engine-one").Change("Alpha");
        cut.Find("#engine-two").Change("Alpha");
        Assert.True(cut.Find("#launch-button").HasAttribute("disabled"));

        cut.Find("#engine-two").Change("Beta");
        Assert.False(cut.Find("#launch-button").HasAttribute("disabled"));
    }

    // ---- Match detail --------------------------------------------------------

    [Fact]
    public void MatchDetail_CompletedMatch_ShowsTheCardAndReplayLink()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1", CannedJson.CompletedMatch));

        var cut = Render<MatchDetail>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha vs Beta", cut.Markup);
            Assert.Contains("3–1", cut.Markup);
            Assert.Equal("matches/match-1/replay", cut.Find("#replay-link").GetAttribute("href"));
        });
    }

    // A terminal match — any of the five terminal statuses — offers the .MAT
    // download beside the replay, pointing at this host's relay (not the
    // internal server). Mirrors how the watch-live link keys off Running.
    // Interrupted (journal-rehydrated) is terminal too: the server serves both
    // its replay and its .MAT.
    [Theory]
    [InlineData("completed")]
    [InlineData("forfeited")]
    [InlineData("aborted")]
    [InlineData("faulted")]
    [InlineData("interrupted")]
    public void MatchDetail_TerminalMatch_OffersTheMatDownload(string status)
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1", TerminalMatchJson(status)));

        var cut = Render<MatchDetail>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
            Assert.Equal("matches/match-1/export.mat", cut.Find("#download-mat-link").GetAttribute("href")));
    }

    [Fact]
    public void MatchDetail_RunningMatch_ShowsWatchLiveInsteadOfReplayOrDownload()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1", CannedJson.RunningMatch));

        var cut = Render<MatchDetail>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("matches/match-1/live", cut.Find("#watch-live-link").GetAttribute("href"));
            Assert.Empty(cut.FindAll("#replay-link"));
            Assert.Empty(cut.FindAll("#download-mat-link"));
        });
    }

    [Fact]
    public void MatchDetail_InterruptedMatch_ShowsTheTerminalCardReplayAndDownload()
    {
        // An interrupted match (journal-rehydrated) is terminal: the detail page
        // renders its status pill and reconstruction detail, offers replay and
        // the .MAT download, and shows no watch-live affordance.
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1", CannedJson.InterruptedMatch));

        var cut = Render<MatchDetail>(p => p.Add(c => c.MatchId, "match-1"));

        cut.WaitForAssertion(() =>
        {
            var pill = cut.Find("dd span.status");
            Assert.Contains("status-interrupted", pill.GetAttribute("class"));
            Assert.Contains("Interrupted", pill.TextContent);
            Assert.Contains("reconstructed from its journal", cut.Markup);
            Assert.Equal("matches/match-1/replay", cut.Find("#replay-link").GetAttribute("href"));
            Assert.Equal("matches/match-1/export.mat", cut.Find("#download-mat-link").GetAttribute("href"));
            Assert.Empty(cut.FindAll("#watch-live-link"));
        });
    }

    /// <summary>A terminal match record with the given status; fields the
    /// affordance does not read are null (a running match is elsewhere).</summary>
    private static string TerminalMatchJson(string status) =>
        $$"""{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"{{status}}","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}""";

    [Fact]
    public void MatchDetail_UnknownId_RendersNotFound()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/nope", "", HttpStatusCode.NotFound));

        var cut = Render<MatchDetail>(p => p.Add(c => c.MatchId, "nope"));

        cut.WaitForAssertion(() => Assert.Contains("No match with id 'nope'", cut.Markup));
    }

    // ---- Tournaments ----------------------------------------------------------

    [Fact]
    public void TournamentsPage_OrderedParticipantsPostInSeedingOrder()
    {
        RoutedJsonHandler handler = UseHandler(new RoutedJsonHandler()
            .Map("GET /engines", CannedJson.TwoIdleEngines)
            .Map("GET /tournaments", "[]")
            .Map("POST /tournaments", CannedJson.RunningTournament));

        var cut = Render<Tournaments>();
        cut.WaitForAssertion(() => Assert.True(cut.FindAll("#participant-pick option").Count > 1));

        cut.Find("#participant-pick").Change("Alpha");
        cut.Find("#add-participant").Click();
        cut.Find("#participant-pick").Change("Beta");
        cut.Find("#add-participant").Click();

        // Move Beta to the top: seeding order is the user's explicit call.
        cut.FindAll("#participant-list li")[1].QuerySelector("button[title='Move up']")!.Click();

        cut.Find("#create-button").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(
                """{"participants":["Beta","Alpha"],"matchLength":5,"matchesPerPairing":2,"seed":null,"timeControl":null}""",
                handler.LastPostBody);
            var navigation = Services.GetRequiredService<NavigationManager>();
            Assert.EndsWith("tournaments/tour-1", navigation.Uri);
        });
    }

    // ---- Tournament detail ----------------------------------------------------

    [Fact]
    public void TournamentDetail_RendersStandingsAndLinkedLedger()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /tournaments/tour-1", CannedJson.RunningTournament));

        var cut = Render<TournamentDetail>(p => p.Add(c => c.TournamentId, "tour-1"));

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll(".standings-table tbody tr").Count);
            var ledgerRows = cut.FindAll(".ledger-table tbody tr");
            Assert.Equal(2, ledgerRows.Count);
            var link = Assert.Single(ledgerRows[0].QuerySelectorAll("a"));
            Assert.Equal("matches/match-1", link.GetAttribute("href"));
            Assert.Contains("scheduled", ledgerRows[1].TextContent);
        });
    }
}
