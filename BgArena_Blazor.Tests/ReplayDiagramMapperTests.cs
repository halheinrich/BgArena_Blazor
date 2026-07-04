using BackgammonDiagram_Lib;
using BgArena_Blazor.Services;
using BgTournament.Api;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Pins the position → DiagramRequest glue over a <see cref="DiagramContext"/>:
/// the fixed seat-One frame (engineOne is always the diagram's positive/on-roll
/// side — nothing flips app-side), the play-vs-cube dice split the Builder
/// validates, the seat-keyed → diagram cube-owner mapping, Crawford flow-through,
/// and the money-session sentinel (MatchLength 0 must build — the needs fields
/// stay 0, never negative). The context is source-agnostic, so these pins hold
/// identically for the settled replay and the live feed.
/// </summary>
public class ReplayDiagramMapperTests
{
    /// <summary>The standard opening position in seat One's frame (the producer golden's fixture).</summary>
    private static readonly int[] OpeningBoard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static GamePosition Position(int cubeValue = 1, CubeOwner cubeOwner = CubeOwner.Centered) =>
        new(OpeningBoard, cubeValue, cubeOwner);

    private static DiagramContext Context(
        int matchLength = 7, int seatOneScore = 0, int seatTwoScore = 0, bool isCrawford = false) =>
        new("Alpha", "Beta", matchLength, seatOneScore, seatTwoScore, isCrawford);

    [Fact]
    public void PlayEntry_MapsToCheckerDiagramCarryingItsDice()
    {
        var entry = new PlayEntry(Seat.Two, Position(), Die1: 3, Die2: 1,
            Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(matchLength: 7, 2, 5), entry);

        Assert.False(request.Decision.IsCube);
        Assert.Equal([3, 1], request.Decision.Dice);
        Assert.Equal(OpeningBoard, request.Position.Mop);
        Assert.Equal(DiagramMode.Problem, request.Mode);
    }

    [Fact]
    public void PlayEntry_AnchorsEngineOneAsTheOnRollSideRegardlessOfActor()
    {
        // The actor is seat Two, but the frame rule is fixed: engineOne is the
        // diagram's on-roll side for every position of the whole match.
        var entry = new PlayEntry(Seat.Two, Position(), Die1: 6, Die2: 2, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(matchLength: 7, 2, 5), entry);

        Assert.Equal("Alpha", request.Descriptive.OnRollName);
        Assert.Equal("Beta", request.Descriptive.OpponentName);
        Assert.Equal(7, request.Descriptive.MatchLength);
        Assert.Equal(5, request.Position.OnRollNeeds);      // 7 − 2, seat One
        Assert.Equal(2, request.Position.OpponentNeeds);    // 7 − 5, seat Two
    }

    [Fact]
    public void DancePlay_IsAnOrdinaryPlayEntryWithDice()
    {
        var entry = new PlayEntry(Seat.One, Position(), Die1: 5, Die2: 5, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.False(request.Decision.IsCube);
        Assert.Equal([5, 5], request.Decision.Dice);
    }

    [Fact]
    public void CubeOfferEntry_MapsToCubeDiagramWithoutDice()
    {
        var entry = new CubeOfferEntry(Seat.Two, Position(cubeValue: 2, CubeOwner.SeatTwo));

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.True(request.Decision.IsCube);
        Assert.Equal([0, 0], request.Decision.Dice);
        Assert.Equal(2, request.Position.CubeSize);
    }

    [Fact]
    public void CubeResponseEntry_MapsToCubeDiagramWithoutDice()
    {
        var entry = new CubeResponseEntry(Seat.One, Position(), CubeResponseAction.Take);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.True(request.Decision.IsCube);
        Assert.Equal([0, 0], request.Decision.Dice);
    }

    [Fact]
    public void FinalState_MapsTheGameEndPositionWithoutDice()
    {
        DiagramRequest request = ReplayDiagramMapper.ForFinalState(
            Context(), Position(cubeValue: 4, CubeOwner.SeatOne));

        Assert.True(request.Decision.IsCube);
        Assert.Equal(4, request.Position.CubeSize);
        Assert.Equal(DiagramCubeOwner.OnRoll, request.Position.CubeOwner);
    }

    [Theory]
    [InlineData(CubeOwner.Centered, DiagramCubeOwner.Centered)]
    [InlineData(CubeOwner.SeatOne, DiagramCubeOwner.OnRoll)]     // seat One = the positive side
    [InlineData(CubeOwner.SeatTwo, DiagramCubeOwner.Opponent)]
    public void CubeOwner_MapsSeatKeyedOntoTheFixedFrame(CubeOwner apiOwner, DiagramCubeOwner expected)
    {
        DiagramRequest request = ReplayDiagramMapper.ForFinalState(
            Context(), Position(cubeValue: 2, apiOwner));

        Assert.Equal(expected, request.Position.CubeOwner);
    }

    [Fact]
    public void Crawford_FlowsThrough()
    {
        var entry = new PlayEntry(Seat.One, Position(), Die1: 2, Die2: 1, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(
            Context(matchLength: 7, seatOneScore: 6, seatTwoScore: 3, isCrawford: true), entry);

        Assert.True(request.Position.IsCrawford);
    }

    [Fact]
    public void MoneySession_BuildsWithTheMoneySentinelAndNoNegativeNeeds()
    {
        // MatchLength 0 is a legitimate producer payload (money session, games
        // cap). The mapper must build — the diagram keys money rendering off
        // MatchLength == 0 and never reads the needs fields on that path.
        var entry = new PlayEntry(Seat.One, Position(), Die1: 4, Die2: 2, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(
            Context(matchLength: 0, seatOneScore: 5, seatTwoScore: 3), entry);

        Assert.Equal(0, request.Descriptive.MatchLength);
        Assert.Equal(0, request.Position.OnRollNeeds);
        Assert.Equal(0, request.Position.OpponentNeeds);
    }

    [Fact]
    public void ForGame_DerivesTheContextFromTheReplayPayload()
    {
        // The settled-replay factory pulls match-level facts from the response
        // and game-level facts from the game — the same six facts the live
        // factory sources from the summary + snapshot.
        var game = new GameReplay(GameNumber: 1, Seat.One, GameResultKind.Single, CubeValue: 1,
            Points: 1, SeatOneScore: 6, SeatTwoScore: 3, IsCrawford: true,
            Entries: [], FinalState: Position());
        var match = new MatchGamesResponse("match-1", "Alpha", "Beta", 7, MatchStatus.Completed, [game]);

        DiagramContext context = DiagramContext.ForGame(match, game);

        Assert.Equal(new DiagramContext("Alpha", "Beta", 7, 6, 3, true), context);
    }

    [Fact]
    public void EveryEntryKindOfOneGame_SurvivesBuilderValidation()
    {
        // The golden-shaped game: play, offer, response, final state — every
        // renderable step of a game must pass DiagramRequest.Builder.Build().
        var entries = new GameEntry[]
        {
            new PlayEntry(Seat.One, Position(), Die1: 3, Die2: 1,
                Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]),
            new CubeOfferEntry(Seat.Two, Position()),
            new CubeResponseEntry(Seat.One, Position(), CubeResponseAction.Take),
        };
        DiagramContext context = Context(matchLength: 3);

        foreach (GameEntry entry in entries)
            Assert.NotNull(ReplayDiagramMapper.ForEntry(context, entry));
        Assert.NotNull(ReplayDiagramMapper.ForFinalState(context, Position(cubeValue: 2, CubeOwner.SeatOne)));
    }
}
