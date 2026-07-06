using System.Net;
using BgArena_Blazor.Components.Pages;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the audit page over the routed transport stub: the
/// golden timeline renders as one type-styled row per event with the
/// narration wording, a clockless (flat-regime) timeline renders gracefully
/// with no clock rows and no legend, the integrity note surfaces, and the
/// documented refusals render distinctly (404 bodyless, 404 with the
/// unreadable-journal reason, 409 offering the live view).
/// </summary>
public class MatchAuditPageTests : BunitContext
{
    private void UseHandler(RoutedJsonHandler handler) =>
        Services.AddSingleton(handler.ToClient());

    private IRenderedComponent<MatchAudit> RenderAudit(string matchId = "match-1") =>
        Render<MatchAudit>(p => p.Add(c => c.MatchId, matchId));

    [Fact]
    public void GoldenAudit_RendersOneTypeStyledRowPerEvent()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", CannedJson.GoldenAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Alpha vs Beta", cut.Find("h1").TextContent);

            var rows = cut.FindAll(".audit-table tbody tr");
            Assert.Equal(10, rows.Count);

            // Each row is type-styled by its wire discriminator.
            Assert.Contains("audit-created", rows[0].GetAttribute("class"));
            Assert.Contains("audit-clock", rows[3].GetAttribute("class"));
            Assert.Contains("audit-terminal", rows[9].GetAttribute("class"));
        });
    }

    [Fact]
    public void GoldenAudit_NarratesEachEventVerbatim()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", CannedJson.GoldenAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            var rows = cut.FindAll(".audit-table tbody tr");

            // created — configuration wording; fair mode names the algorithm,
            // never the recorded seed (it is not a reproduction key).
            Assert.Contains(
                "Match created — 3 points, Fischer 120s + 8s/decision, fair dice (hmac-sha256-dice-v1)",
                rows[0].TextContent);
            Assert.DoesNotContain("seed", rows[0].TextContent);

            Assert.Contains("Wire start — matchStarted sent to both engines", rows[1].TextContent);
            Assert.Contains("Game 1 — Alpha 0 · Beta 0", rows[2].TextContent);

            // clock — the arithmetic verbatim, plus the game reference.
            Assert.Contains(
                "Alpha — play decision: 12.5s think, pool 120s → 115.5s, increment credited",
                rows[3].TextContent);
            Assert.Contains("game 1", rows[3].TextContent);

            // decision events carry their replay join (game + 0-based entry).
            Assert.Contains("Alpha rolls 3-1", rows[4].TextContent);
            Assert.Contains("game 1 · entry 0", rows[4].TextContent);
            Assert.Contains("Beta offers a double", rows[5].TextContent);
            Assert.Contains("game 1 · entry 1", rows[5].TextContent);
            Assert.Contains("Alpha takes", rows[6].TextContent);
            Assert.Contains("game 1 · entry 2", rows[6].TextContent);

            Assert.Contains(
                "Beta answered an abandoned query too late — reply discarded (request q-9)",
                rows[7].TextContent);
            Assert.Contains("Game 1 — Beta wins a single at cube 2", rows[8].TextContent);
            Assert.Contains("Match completed — Beta wins 1–3", rows[9].TextContent);

            // Row timestamps are the events' UTC times of day.
            Assert.Equal("12:00:00", rows[0].QuerySelector(".audit-time")!.TextContent);
            Assert.Equal("12:30:00", rows[9].QuerySelector(".audit-time")!.TextContent);
        });
    }

    [Fact]
    public void GoldenAudit_RendersTheFairDiceFieldsVerbatim()
    {
        // Commitment and revealed key render as-is — the packet is an external
        // arbiter's self-contained input; no verification logic app-side.
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", CannedJson.GoldenAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            var hexes = cut.FindAll(".audit-hex code");
            Assert.Equal(2, hexes.Count);
            Assert.Equal(
                "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100",
                hexes[0].TextContent);
            Assert.Equal(
                "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
                hexes[1].TextContent);
        });
    }

    [Fact]
    public void ClockedTimeline_ShowsTheCorrelationLegend()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", CannedJson.GoldenAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
            Assert.Contains("a clock row precedes the decision it timed",
                cut.Find("#clock-legend").TextContent));
    }

    [Fact]
    public void ClocklessTimeline_RendersGracefully_NoClockRowsAndNoLegend()
    {
        // A flat-regime match records no per-decision timing: no clock rows is
        // the normal shape, not a gap — the timeline renders without a legend.
        const string flatAudit =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"completed","integrity":null,"events":[{"type":"created","at":"2026-07-05T12:00:00+00:00","matchLength":1,"maxGames":null,"seed":42,"diceAlgorithm":null,"diceCommitment":null,"timeControl":null},{"type":"started","at":"2026-07-05T12:00:00+00:00"},{"type":"gameStarted","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"seatOneScore":0,"seatTwoScore":0,"isCrawford":false},{"type":"play","at":"2026-07-05T12:00:00+00:00","gameNumber":1,"entryIndex":0,"actor":"seatOne","die1":3,"die2":1},{"type":"gameEnded","at":"2026-07-05T12:10:00+00:00","gameNumber":1,"winner":"seatOne","resultKind":"single","cubeValue":1},{"type":"terminal","at":"2026-07-05T12:10:00+00:00","status":"completed","winner":"Alpha","seatOneScore":1,"seatTwoScore":0,"forfeitedBy":null,"forfeitCause":null,"detail":null,"diceAlgorithm":null,"diceKey":null}]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", flatAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(6, cut.FindAll(".audit-table tbody tr").Count);
            Assert.Empty(cut.FindAll(".audit-clock"));
            Assert.Empty(cut.FindAll("#clock-legend"));

            // An explicit-seed match names its seed and the flat regime.
            Assert.Contains("Match created — 1 point, flat per-decision timeout, seed 42", cut.Markup);
            Assert.Empty(cut.FindAll(".audit-hex"));
        });
    }

    [Fact]
    public void ForfeitedTimeline_NarratesTheStructuredCauseAndDetail()
    {
        const string forfeitedAudit =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"forfeited","integrity":null,"events":[{"type":"terminal","at":"2026-07-05T12:30:00+00:00","status":"forfeited","winner":"Beta","seatOneScore":null,"seatTwoScore":null,"forfeitedBy":"Alpha","forfeitCause":"flagFall","detail":"Engine 'Alpha' ran out of time on a play query (flag fall).","diceAlgorithm":null,"diceKey":null}]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", forfeitedAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll(".audit-table tbody tr"));
            Assert.Contains("Match forfeited by Alpha — flag fall; Beta wins", row.TextContent);
            // The prose detail stays the human-readable companion, beneath.
            Assert.Contains("ran out of time on a play query", row.QuerySelector(".audit-detail")!.TextContent);
        });
    }

    [Fact]
    public void InterruptedTimeline_TerminalRowHasNoTimestamp()
    {
        // An Interrupted match's terminal event honestly carries no time (it
        // died with the server) — and reveals the escrowed fair-dice key.
        const string interruptedAudit =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"interrupted","integrity":null,"events":[{"type":"terminal","at":null,"status":"interrupted","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"forfeitCause":null,"detail":"The server was interrupted while this match was running; the record was reconstructed from its journal.","diceAlgorithm":"hmac-sha256-dice-v1","diceKey":"00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff"}]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", interruptedAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll(".audit-table tbody tr"));
            Assert.Equal("—", row.QuerySelector(".audit-time")!.TextContent);
            Assert.Contains("Match interrupted", row.TextContent);
            Assert.Contains("00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
                row.QuerySelector(".audit-hex code")!.TextContent);
        });
    }

    [Fact]
    public void IntegrityNote_RendersTheTrustedPrefixWarning()
    {
        const string damagedAudit =
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"interrupted","integrity":"journal corrupt after line 12","events":[{"type":"terminal","at":null,"status":"interrupted","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"forfeitCause":null,"detail":null,"diceAlgorithm":null,"diceKey":null}]}""";
        UseHandler(new RoutedJsonHandler().Map("GET /matches/match-1/audit", damagedAudit));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            string note = cut.Find("#audit-integrity").TextContent;
            Assert.Contains("journal corrupt after line 12", note);
            Assert.Contains("trusted prefix", note);
        });
    }

    [Fact]
    public void UnknownId_RendersNotFound()
    {
        UseHandler(new RoutedJsonHandler().Map("GET /matches/nope/audit", "", HttpStatusCode.NotFound));

        var cut = RenderAudit("nope");

        cut.WaitForAssertion(() => Assert.Contains("No match with id 'nope'", cut.Markup));
    }

    [Fact]
    public void UnreadableJournal_RendersTheServersReason()
    {
        // The endpoint's second documented 404 flavor: the record exists but
        // its journal cannot be read — the reason names it, not "unknown id".
        UseHandler(new RoutedJsonHandler().Map(
            "GET /matches/match-1/audit",
            """{"error":"Match 'match-1' has no readable audit journal."}""",
            HttpStatusCode.NotFound));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("has no readable audit journal", cut.Markup);
            Assert.DoesNotContain("No match with id", cut.Markup);
        });
    }

    [Fact]
    public void StillRunning_RendersTheReasonWithLiveLinkAndRetry()
    {
        UseHandler(new RoutedJsonHandler().Map(
            "GET /matches/match-1/audit",
            """{"error":"Match 'match-1' is still running; watch it at /matches/match-1/live, or audit it once it ends."}""",
            HttpStatusCode.Conflict));

        var cut = RenderAudit();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("still running", cut.Find("#audit-refused").TextContent);
            Assert.Equal("matches/match-1/live", cut.Find("#audit-live-link").GetAttribute("href"));
            Assert.NotNull(cut.Find("#retry-audit"));
        });
    }
}
