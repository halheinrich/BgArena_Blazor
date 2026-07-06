using System.Net;
using System.Text;
using BgArena_Blazor.Services;
using BgTournament.Api;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Exercises ArenaClient against canned HTTP responses whose JSON mirrors the
/// producer's golden pins (ApiGoldenTests) — the real deserialization path
/// with Web defaults and zero converter config, stubbed at the transport
/// layer rather than behind an interface. Covers the refusal contract:
/// documented statuses fold into ArenaResult (with the ErrorResponse reason
/// when a body is present), undocumented statuses throw.
/// </summary>
public class ArenaClientTests
{
    /// <summary>The producer golden's start/end instants (see ApiGoldenTests).</summary>
    private static readonly DateTimeOffset StartedAt = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset EndedAt = new(2026, 7, 5, 12, 30, 0, TimeSpan.Zero);

    /// <summary>Captures the last request and answers from a scripted responder.</summary>
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static ArenaClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://arena.test") });

    // ---- listings ----------------------------------------------------------

    [Fact]
    public async Task GetEngines_DeserializesTheListing()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[{"name":"Alpha","version":"1.2","author":"Hal","inMatch":true},{"name":"Beta","version":null,"author":null,"inMatch":false}]"""));

        IReadOnlyList<EngineSummary> engines = await Client(handler).GetEnginesAsync();

        Assert.Equal("/engines", handler.LastRequestUri?.AbsolutePath);
        Assert.Equal(
            [new EngineSummary("Alpha", "1.2", "Hal", InMatch: true),
             new EngineSummary("Beta", null, null, InMatch: false)],
            engines);
    }

    [Fact]
    public async Task GetMatches_DeserializesEnumStringsWithWebDefaults()
    {
        // The completed-match golden row, verbatim from the producer's pins.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """[{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"timeControl":null,"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"forfeitCause":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":"2026-07-05T12:30:00+00:00"}]"""));

        IReadOnlyList<MatchSummary> matches = await Client(handler).GetMatchesAsync();

        MatchSummary match = Assert.Single(matches);
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Equal(
            new MatchSummary("match-1", "Alpha", "Beta", MatchLength: 3, MaxGames: null, Seed: 42,
                TimeControl: null, MatchStatus.Completed, Winner: "Alpha", SeatOneScore: 3, SeatTwoScore: 1,
                ForfeitedBy: null, ForfeitCause: null, Detail: null, StartedAtUtc: StartedAt, EndedAtUtc: EndedAt),
            match);
    }

    // ---- by-id refusals ----------------------------------------------------

    [Fact]
    public async Task GetMatch_NotFound_IsARefusalWithoutAReason()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        ArenaResult<MatchSummary> result = await Client(handler).GetMatchAsync("nope");

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Null(result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public async Task GetMatch_EscapesTheIdIntoThePath()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        _ = await Client(handler).GetMatchAsync("a/b c");

        Assert.Equal("/matches/a%2Fb%20c", handler.LastRequestUri?.AbsolutePath);
    }

    // ---- replay ------------------------------------------------------------

    [Fact]
    public async Task GetMatchGames_DeserializesTheGoldenReplayShape()
    {
        // The producer's whole-replay golden pin, verbatim: all three entry
        // kinds under their "type" discriminators.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CannedJson.GoldenReplay));

        ArenaResult<MatchGamesResponse> result = await Client(handler).GetMatchGamesAsync("match-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("/matches/match-1/games", handler.LastRequestUri?.AbsolutePath);

        MatchGamesResponse replay = result.Value;
        Assert.Equal(("Alpha", "Beta", 3), (replay.EngineOne, replay.EngineTwo, replay.MatchLength));
        GameReplay game = Assert.Single(replay.Games);
        Assert.Equal(Seat.Two, game.Winner);
        Assert.Equal(GameResultKind.Single, game.ResultKind);

        Assert.Collection(game.Entries,
            entry =>
            {
                PlayEntry play = Assert.IsType<PlayEntry>(entry);
                Assert.Equal((Seat.One, 3, 1), (play.Actor, play.Die1, play.Die2));
                Assert.Equal([new PlayMove(8, 5), new PlayMove(6, 5)], play.Moves);
            },
            entry => Assert.Equal(Seat.Two, Assert.IsType<CubeOfferEntry>(entry).Actor),
            entry => Assert.Equal(CubeResponseAction.Take, Assert.IsType<CubeResponseEntry>(entry).Action));

        Assert.Equal(2, game.FinalState.CubeValue);
        Assert.Equal(CubeOwner.SeatOne, game.FinalState.CubeOwner);
    }

    [Fact]
    public async Task GetMatchGames_Conflict_SurfacesTheUnescapedReason()
    {
        // Detail strings carry ' for apostrophes on the wire (Web-default
        // encoder); System.Text.Json unescapes on read.
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict,
            """{"error":"Match 'match-1' is not completed: running."}"""));

        ArenaResult<MatchGamesResponse> result = await Client(handler).GetMatchGamesAsync("match-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("Match 'match-1' is not completed: running.", result.Error);
    }

    // ---- audit -------------------------------------------------------------

    [Fact]
    public async Task GetMatchAudit_DeserializesTheGoldenAuditShape()
    {
        // The producer's whole-audit-timeline golden pin, verbatim: every
        // event kind under its "type" discriminator.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, CannedJson.GoldenAudit));

        ArenaResult<MatchAuditResponse> result = await Client(handler).GetMatchAuditAsync("match-1");

        Assert.True(result.IsSuccess);
        Assert.Equal("/matches/match-1/audit", handler.LastRequestUri?.AbsolutePath);

        MatchAuditResponse audit = result.Value;
        Assert.Equal(("match-1", "Alpha", "Beta"), (audit.MatchId, audit.EngineOne, audit.EngineTwo));
        Assert.Equal(MatchStatus.Completed, audit.Status);
        Assert.Null(audit.Integrity);

        Assert.Collection(audit.Events,
            e =>
            {
                AuditCreatedEvent created = Assert.IsType<AuditCreatedEvent>(e);
                Assert.Equal((3, 42), (created.MatchLength, created.Seed));
                Assert.Equal("hmac-sha256-dice-v1", created.DiceAlgorithm);
                Assert.Equal(
                    "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100",
                    created.DiceCommitment);
                Assert.Equal(new TimeControl(120, 8), created.TimeControl);
            },
            e => Assert.Equal(StartedAt, Assert.IsType<AuditStartedEvent>(e).At),
            e =>
            {
                AuditGameStartedEvent started = Assert.IsType<AuditGameStartedEvent>(e);
                Assert.Equal((1, 0, 0, false),
                    (started.GameNumber, started.SeatOneScore, started.SeatTwoScore, started.IsCrawford));
            },
            e =>
            {
                AuditClockEvent clock = Assert.IsType<AuditClockEvent>(e);
                Assert.Equal((Seat.One, DecisionKind.Play, true), (clock.Seat, clock.Decision, clock.IncrementCredited));
                Assert.Equal((12.5, 120d, 115.5),
                    (clock.ThinkSeconds, clock.RemainingBeforeSeconds, clock.RemainingAfterSeconds));
            },
            e =>
            {
                AuditPlayEvent play = Assert.IsType<AuditPlayEvent>(e);
                Assert.Equal((1, 0, Seat.One, 3, 1),
                    (play.GameNumber, play.EntryIndex, play.Actor, play.Die1, play.Die2));
            },
            e => Assert.Equal((1, Seat.Two), (Assert.IsType<AuditCubeOfferEvent>(e).EntryIndex, ((AuditCubeOfferEvent)e).Actor)),
            e => Assert.Equal(CubeResponseAction.Take, Assert.IsType<AuditCubeResponseEvent>(e).Action),
            e => Assert.Equal((Seat.Two, "q-9"), (Assert.IsType<AuditLateReplyEvent>(e).Seat, ((AuditLateReplyEvent)e).RequestId)),
            e =>
            {
                AuditGameEndedEvent ended = Assert.IsType<AuditGameEndedEvent>(e);
                Assert.Equal((Seat.Two, GameResultKind.Single, 2), (ended.Winner, ended.ResultKind, ended.CubeValue));
            },
            e =>
            {
                AuditTerminalEvent terminal = Assert.IsType<AuditTerminalEvent>(e);
                Assert.Equal((MatchStatus.Completed, "Beta", 1, 3), (terminal.Status, terminal.Winner, terminal.SeatOneScore, terminal.SeatTwoScore));
                Assert.Null(terminal.ForfeitCause);
                Assert.Equal(
                    "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff",
                    terminal.DiceKey);
            });
    }

    [Fact]
    public async Task GetMatchAudit_TerminalForfeit_CarriesTheStructuredCause()
    {
        // The producer's forfeit golden: the structured cause is the headline
        // datum beside the prose detail.
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","status":"forfeited","integrity":null,"events":[{"type":"terminal","at":"2026-07-05T12:30:00+00:00","status":"forfeited","winner":"Beta","seatOneScore":null,"seatTwoScore":null,"forfeitedBy":"Alpha","forfeitCause":"flagFall","detail":"Engine 'Alpha' ran out of time on a play query (flag fall).","diceAlgorithm":null,"diceKey":null}]}"""));

        ArenaResult<MatchAuditResponse> result = await Client(handler).GetMatchAuditAsync("match-1");

        AuditTerminalEvent terminal = Assert.IsType<AuditTerminalEvent>(Assert.Single(result.Value.Events));
        Assert.Equal("Alpha", terminal.ForfeitedBy);
        Assert.Equal(ForfeitCause.FlagFall, terminal.ForfeitCause);
    }

    [Fact]
    public async Task GetMatchAudit_NotFound_IsARefusalWithoutAReason()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        ArenaResult<MatchAuditResponse> result = await Client(handler).GetMatchAuditAsync("nope");

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task GetMatchAudit_NotFoundWithBody_CarriesTheUnreadableJournalReason()
    {
        // The audit endpoint's second documented 404 flavor: the record exists
        // but its journal cannot be read — the refusal carries a reason.
        var handler = new StubHandler(_ => Json(HttpStatusCode.NotFound,
            """{"error":"Match 'match-1' has no readable audit journal."}"""));

        ArenaResult<MatchAuditResponse> result = await Client(handler).GetMatchAuditAsync("match-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        Assert.Equal("Match 'match-1' has no readable audit journal.", result.Error);
    }

    [Fact]
    public async Task GetMatchAudit_Conflict_SurfacesTheStillRunningReason()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict,
            """{"error":"Match 'match-1' is still running; watch it at /matches/match-1/live, or audit it once it ends."}"""));

        ArenaResult<MatchAuditResponse> result = await Client(handler).GetMatchAuditAsync("match-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Contains("still running", result.Error);
    }

    // ---- starting ----------------------------------------------------------

    [Fact]
    public async Task StartMatch_PostsTheGoldenRequestShapeAndReadsTheSummary()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"maxGames":50,"seed":42,"timeControl":null,"status":"running","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"forfeitCause":null,"detail":null,"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}"""));

        ArenaResult<MatchSummary> result = await Client(handler)
            .StartMatchAsync(new StartMatchRequest("Alpha", "Beta", MatchLength: 7, Seed: 42, MaxGames: 50));

        Assert.Equal("/matches", handler.LastRequestUri?.AbsolutePath);
        // The serialized body must be the producer golden's request text.
        Assert.Equal(
            """{"engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"seed":42,"maxGames":50,"timeControl":null}""",
            handler.LastRequestBody);
        Assert.True(result.IsSuccess);
        Assert.Equal(MatchStatus.Running, result.Value.Status);
    }

    [Fact]
    public async Task StartMatch_Conflict_IsARefusalCarryingTheReason()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict,
            """{"error":"Engine 'Alpha' is already in a match."}"""));

        ArenaResult<MatchSummary> result = await Client(handler)
            .StartMatchAsync(new StartMatchRequest("Alpha", "Beta", MatchLength: 7));

        Assert.False(result.IsSuccess);
        Assert.Equal("Engine 'Alpha' is already in a match.", result.Error);
    }

    [Fact]
    public async Task StartTournament_PostsAndReadsTheSummary()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"tournamentId":"tour-1","participants":["Alpha","Beta"],"matchLength":3,"matchesPerPairing":2,"seed":7,"timeControl":null,"status":"running","winner":null,"detail":null,"standings":[{"rank":1,"participant":"Alpha","wins":0,"losses":0,"sonnebornBerger":0},{"rank":2,"participant":"Beta","wins":0,"losses":0,"sonnebornBerger":0}],"matches":[{"index":0,"seatOne":"Alpha","seatTwo":"Beta","seed":11,"matchId":null,"status":null,"winner":null}],"startedAtUtc":"2026-07-05T12:00:00+00:00","endedAtUtc":null}"""));

        ArenaResult<TournamentSummary> result = await Client(handler)
            .StartTournamentAsync(new StartTournamentRequest(["Alpha", "Beta"], MatchLength: 3, MatchesPerPairing: 2, Seed: 7));

        Assert.True(result.IsSuccess);
        Assert.Equal(TournamentStatus.Running, result.Value.Status);
        Assert.Equal(2, result.Value.Standings.Count);
        TournamentMatchEntry ledgerRow = Assert.Single(result.Value.Matches);
        Assert.Null(ledgerRow.MatchId);   // not reached yet — null until the tournament gets there
    }

    // ---- undocumented statuses --------------------------------------------

    [Fact]
    public async Task UndocumentedStatus_Throws()
    {
        var handler = new StubHandler(_ =>
            Json(HttpStatusCode.InternalServerError, """{"error":"boom"}"""));

        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetMatchAsync("match-1"));
    }
}
