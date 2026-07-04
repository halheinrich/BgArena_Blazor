using BackgammonDiagram_Lib;
using BgArena_Blazor.Services;
using BgTournament.Api;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Pins the GameEntry/GamePosition → DiagramRequest glue: the fixed seat-One
/// frame (engineOne is always the diagram's positive/on-roll side — nothing
/// flips app-side), the play-vs-cube dice split the Builder validates, the
/// seat-keyed → diagram cube-owner mapping, and the money-session sentinel
/// (MatchLength 0 must build — the needs fields stay 0, never negative).
/// </summary>
public class ReplayDiagramMapperTests
{
    /// <summary>The standard opening position in seat One's frame (the producer golden's fixture).</summary>
    private static readonly int[] OpeningBoard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static GamePosition Position(int cubeValue = 1, CubeOwner cubeOwner = CubeOwner.Centered) =>
        new(OpeningBoard, cubeValue, cubeOwner);

    private static GameReplay Game(
        int seatOneScore = 0, int seatTwoScore = 0, bool isCrawford = false,
        IReadOnlyList<GameEntry>? entries = null, GamePosition? finalState = null) =>
        new(GameNumber: 1, Seat.One, GameResultKind.Single, CubeValue: 1, Points: 1,
            seatOneScore, seatTwoScore, isCrawford,
            entries ?? [], finalState ?? Position());

    private static MatchGamesResponse Match(int matchLength = 7, GameReplay? game = null) =>
        new("match-1", "Alpha", "Beta", matchLength, [game ?? Game()]);

    [Fact]
    public void PlayEntry_MapsToCheckerDiagramCarryingItsDice()
    {
        var entry = new PlayEntry(Seat.Two, Position(), Die1: 3, Die2: 1,
            Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]);
        var game = Game(seatOneScore: 2, seatTwoScore: 5, entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(matchLength: 7, game), game, entry);

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
        var game = Game(seatOneScore: 2, seatTwoScore: 5, entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(matchLength: 7, game), game, entry);

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
        var game = Game(entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(game: game), game, entry);

        Assert.False(request.Decision.IsCube);
        Assert.Equal([5, 5], request.Decision.Dice);
    }

    [Fact]
    public void CubeOfferEntry_MapsToCubeDiagramWithoutDice()
    {
        var entry = new CubeOfferEntry(Seat.Two, Position(cubeValue: 2, CubeOwner.SeatTwo));
        var game = Game(entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(game: game), game, entry);

        Assert.True(request.Decision.IsCube);
        Assert.Equal([0, 0], request.Decision.Dice);
        Assert.Equal(2, request.Position.CubeSize);
    }

    [Fact]
    public void CubeResponseEntry_MapsToCubeDiagramWithoutDice()
    {
        var entry = new CubeResponseEntry(Seat.One, Position(), CubeResponseAction.Take);
        var game = Game(entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(game: game), game, entry);

        Assert.True(request.Decision.IsCube);
        Assert.Equal([0, 0], request.Decision.Dice);
    }

    [Fact]
    public void FinalState_MapsTheGameEndPositionWithoutDice()
    {
        var game = Game(finalState: Position(cubeValue: 4, CubeOwner.SeatOne));

        DiagramRequest request = ReplayDiagramMapper.ForFinalState(Match(game: game), game);

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
        var game = Game(finalState: Position(cubeValue: 2, apiOwner));

        DiagramRequest request = ReplayDiagramMapper.ForFinalState(Match(game: game), game);

        Assert.Equal(expected, request.Position.CubeOwner);
    }

    [Fact]
    public void Crawford_FlowsThrough()
    {
        var entry = new PlayEntry(Seat.One, Position(), Die1: 2, Die2: 1, Moves: []);
        var game = Game(seatOneScore: 6, seatTwoScore: 3, isCrawford: true, entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(matchLength: 7, game), game, entry);

        Assert.True(request.Position.IsCrawford);
    }

    [Fact]
    public void MoneySession_BuildsWithTheMoneySentinelAndNoNegativeNeeds()
    {
        // MatchLength 0 is a legitimate producer payload (money session, games
        // cap). The mapper must build — the diagram keys money rendering off
        // MatchLength == 0 and never reads the needs fields on that path.
        var entry = new PlayEntry(Seat.One, Position(), Die1: 4, Die2: 2, Moves: []);
        var game = Game(seatOneScore: 5, seatTwoScore: 3, entries: [entry]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Match(matchLength: 0, game), game, entry);

        Assert.Equal(0, request.Descriptive.MatchLength);
        Assert.Equal(0, request.Position.OnRollNeeds);
        Assert.Equal(0, request.Position.OpponentNeeds);
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
        var game = Game(entries: entries, finalState: Position(cubeValue: 2, CubeOwner.SeatOne));
        var match = Match(matchLength: 3, game);

        foreach (GameEntry entry in entries)
            Assert.NotNull(ReplayDiagramMapper.ForEntry(match, game, entry));
        Assert.NotNull(ReplayDiagramMapper.ForFinalState(match, game));
    }
}
