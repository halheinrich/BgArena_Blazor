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
            """[{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":3,"maxGames":null,"seed":42,"status":"completed","winner":"Alpha","seatOneScore":3,"seatTwoScore":1,"forfeitedBy":null,"detail":null}]"""));

        IReadOnlyList<MatchSummary> matches = await Client(handler).GetMatchesAsync();

        MatchSummary match = Assert.Single(matches);
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Equal(
            new MatchSummary("match-1", "Alpha", "Beta", MatchLength: 3, MaxGames: null, Seed: 42,
                MatchStatus.Completed, Winner: "Alpha", SeatOneScore: 3, SeatTwoScore: 1,
                ForfeitedBy: null, Detail: null),
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

    // ---- starting ----------------------------------------------------------

    [Fact]
    public async Task StartMatch_PostsTheGoldenRequestShapeAndReadsTheSummary()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"matchId":"match-1","engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"maxGames":50,"seed":42,"status":"running","winner":null,"seatOneScore":null,"seatTwoScore":null,"forfeitedBy":null,"detail":null}"""));

        ArenaResult<MatchSummary> result = await Client(handler)
            .StartMatchAsync(new StartMatchRequest("Alpha", "Beta", MatchLength: 7, Seed: 42, MaxGames: 50));

        Assert.Equal("/matches", handler.LastRequestUri?.AbsolutePath);
        // The serialized body must be the producer golden's request text.
        Assert.Equal(
            """{"engineOne":"Alpha","engineTwo":"Beta","matchLength":7,"seed":42,"maxGames":50}""",
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
            """{"tournamentId":"tour-1","participants":["Alpha","Beta"],"matchLength":3,"matchesPerPairing":2,"seed":7,"status":"running","winner":null,"detail":null,"standings":[{"rank":1,"participant":"Alpha","wins":0,"losses":0,"sonnebornBerger":0},{"rank":2,"participant":"Beta","wins":0,"losses":0,"sonnebornBerger":0}],"matches":[{"index":0,"seatOne":"Alpha","seatTwo":"Beta","seed":11,"matchId":null,"status":null,"winner":null}]}"""));

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
