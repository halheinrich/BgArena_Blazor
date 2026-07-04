using BgArena_Blazor.Components.Shared;
using BgTournament.Api;
using Bunit;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the shared display tables: row content, link
/// targets, the money-length wording, and the unreached-ledger-row shape.
/// </summary>
public class SharedTableTests : BunitContext
{
    private static MatchSummary Match(
        string id = "match-1", int matchLength = 7, int? maxGames = null,
        MatchStatus status = MatchStatus.Running, string? winner = null,
        int? seatOneScore = null, int? seatTwoScore = null) =>
        new(id, "Alpha", "Beta", matchLength, maxGames, Seed: 42, status, winner,
            seatOneScore, seatTwoScore, ForfeitedBy: null, Detail: null);

    [Fact]
    public void MatchesTable_RendersARowPerMatchLinkingToDetail()
    {
        var cut = Render<MatchesTable>(p => p.Add(c => c.Matches,
        [
            Match(id: "m-1"),
            Match(id: "m-2", status: MatchStatus.Completed, winner: "Alpha", seatOneScore: 7, seatTwoScore: 3),
        ]));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal("matches/m-1", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("matches/m-2", rows[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("7–3", cut.Markup);
        Assert.Contains("Alpha vs Beta", cut.Markup);
    }

    [Fact]
    public void MatchesTable_OffersWatchLiveOnRunningRowsOnly()
    {
        var cut = Render<MatchesTable>(p => p.Add(c => c.Matches,
        [
            Match(id: "m-run"),
            Match(id: "m-done", status: MatchStatus.Completed, winner: "Alpha", seatOneScore: 7, seatTwoScore: 3),
        ]));

        var rows = cut.FindAll("tbody tr");
        var watch = rows[0].QuerySelector("a.watch-live");
        Assert.NotNull(watch);
        Assert.Equal("matches/m-run/live", watch.GetAttribute("href"));
        Assert.Null(rows[1].QuerySelector("a.watch-live"));
    }

    [Fact]
    public void MatchesTable_WordsAMoneySessionByItsGamesCap()
    {
        var cut = Render<MatchesTable>(p => p.Add(c => c.Matches,
            [Match(matchLength: 0, maxGames: 50)]));

        Assert.Contains("money · 50 games", cut.Markup);
    }

    [Fact]
    public void MatchesTable_EmptyListRendersTheEmptyMessage()
    {
        var cut = Render<MatchesTable>(p => p.Add(c => c.Matches, []));

        Assert.Empty(cut.FindAll("table"));
        Assert.Contains("No matches yet", cut.Markup);
    }

    [Fact]
    public void StandingsTable_RendersLinesInGivenOrder()
    {
        var cut = Render<StandingsTable>(p => p.Add(c => c.Standings,
        [
            new StandingEntry(1, "Alpha", Wins: 3, Losses: 1, SonnebornBerger: 2),
            new StandingEntry(2, "Beta", Wins: 1, Losses: 3, SonnebornBerger: 1),
        ]));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        Assert.Contains("Alpha", rows[0].TextContent);
        Assert.Contains("Beta", rows[1].TextContent);
    }

    [Fact]
    public void TournamentLedger_LinksReachedRowsAndMarksUnreachedScheduled()
    {
        var cut = Render<TournamentLedger>(p => p.Add(c => c.Entries,
        [
            new TournamentMatchEntry(0, "Alpha", "Beta", Seed: 11,
                MatchId: "match-1", MatchStatus.Completed, Winner: "Alpha"),
            new TournamentMatchEntry(1, "Beta", "Alpha", Seed: 12,
                MatchId: null, Status: null, Winner: null),
        ]));

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);

        var link = Assert.Single(rows[0].QuerySelectorAll("a"));
        Assert.Equal("matches/match-1", link.GetAttribute("href"));

        Assert.Empty(rows[1].QuerySelectorAll("a"));
        Assert.Contains("scheduled", rows[1].TextContent);
    }
}
