using BackgammonDiagram_Lib;
using BgArena_Blazor.Services;
using BgTournament.Api;
using ApiCubeOwner = BgTournament.Api.CubeOwner;
using BoardPosition = BgDataTypes_Lib.BoardPosition;
using DiagramCubeOwner = BgDataTypes_Lib.CubeOwner;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Pins the position → board request glue over a <see cref="DiagramContext"/>.
/// Every arena board is a <see cref="DiagramRequest.ForBoard"/> request — a
/// served <see cref="BoardPosition"/> and <see cref="DisplayFacts"/>, never a
/// decision — in the fixed seat-One frame (engineOne is always the diagram's
/// positive/on-roll side; nothing flips app-side). A play entry shows its
/// dice; a cube entry and the final state show none, and no board states a
/// title or any other decision-domain text. The facts stated are the API's:
/// a match board's away scores and the game's Crawford flag, a money board's
/// label with no Jacoby rule (the API states none). The context is
/// source-agnostic, so these pins hold identically for the settled replay and
/// the live feed.
/// </summary>
public class ReplayDiagramMapperTests
{
    /// <summary>The standard opening position in seat One's frame (the producer golden's fixture).</summary>
    private static readonly int[] OpeningBoard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static GamePosition Position(int cubeValue = 1, ApiCubeOwner cubeOwner = ApiCubeOwner.Centered) =>
        new(OpeningBoard, cubeValue, cubeOwner);

    private static DiagramContext Context(
        int matchLength = 7, int seatOneScore = 0, int seatTwoScore = 0, bool isCrawford = false) =>
        new("Alpha", "Beta", matchLength, seatOneScore, seatTwoScore, isCrawford);

    /// <summary>The facts every board of a 7-point game at 0–0 states, before its own dice and cube.</summary>
    private static DisplayFacts SevenPointFacts() => new()
    {
        OnRollName = "Alpha",
        OpponentName = "Beta",
        Score = new MatchRailScore(onRollNeeds: 7, opponentNeeds: 7, isCrawford: false),
    };

    [Fact]
    public void PlayEntry_IsABoardRequestShowingItsDiceInServedOrder()
    {
        var entry = new PlayEntry(Seat.Two, Position(), Die1: 3, Die2: 1,
            Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.Null(request.Decision);
        Assert.Equal(new BoardPosition(OpeningBoard), request.Board);
        Assert.Equal(DiagramMode.Problem, request.Mode);
        Assert.Equal(SevenPointFacts() with { Dice = new DiceFaces(3, 1) }, request.Display);
    }

    [Fact]
    public void PlayEntry_AnchorsEngineOneAsTheOnRollSideRegardlessOfActor()
    {
        // The actor is seat Two, but the frame rule is fixed: engineOne is the
        // diagram's on-roll side, and its away score the on-roll side's, for
        // every position of the whole match.
        var entry = new PlayEntry(Seat.Two, Position(), Die1: 6, Die2: 2, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(matchLength: 7, 2, 5), entry);

        DisplayFacts facts = request.Display!;
        Assert.Equal("Alpha", facts.OnRollName);
        Assert.Equal("Beta", facts.OpponentName);
        Assert.Equal(new MatchRailScore(onRollNeeds: 5, opponentNeeds: 2, isCrawford: false), facts.Score);
    }

    [Fact]
    public void DancePlay_IsAnOrdinaryPlayEntryWithDice()
    {
        var entry = new PlayEntry(Seat.One, Position(), Die1: 5, Die2: 5, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.Equal(new DiceFaces(5, 5), request.Display!.Dice);
    }

    [Fact]
    public void CubeOfferEntry_IsABoardWithNoDiceAndNothingOfADecision()
    {
        var entry = new CubeOfferEntry(Seat.Two, Position(cubeValue: 2, ApiCubeOwner.SeatTwo));

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        // Whole-facts equality: no dice and no title — nothing is stated to
        // stand in for the decision-only "Cube Action?".
        Assert.Null(request.Decision);
        Assert.Equal(
            SevenPointFacts() with { CubeValue = 2, CubeOwner = DiagramCubeOwner.Opponent },
            request.Display);
    }

    [Fact]
    public void CubeResponseEntry_IsABoardWithNoDiceAndNothingOfADecision()
    {
        var entry = new CubeResponseEntry(Seat.One, Position(), CubeResponseAction.Take);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(Context(), entry);

        Assert.Null(request.Decision);
        Assert.Equal(SevenPointFacts(), request.Display);
    }

    [Fact]
    public void FinalState_IsABoardWithNoDiceAndNothingOfADecision()
    {
        DiagramRequest request = ReplayDiagramMapper.ForFinalState(
            Context(), Position(cubeValue: 4, ApiCubeOwner.SeatOne));

        Assert.Null(request.Decision);
        Assert.Equal(
            SevenPointFacts() with { CubeValue = 4, CubeOwner = DiagramCubeOwner.OnRoll },
            request.Display);
    }

    [Fact]
    public void FinalState_DrawsATerminalBoard()
    {
        // A game's final position has a side borne off — no decision position,
        // but a well-formed board, which is all ForBoard asks.
        int[] seatOneBorneOff = new int[26];
        seatOneBorneOff[6] = -15;

        DiagramRequest request = ReplayDiagramMapper.ForFinalState(
            Context(), new GamePosition(seatOneBorneOff, 1, ApiCubeOwner.Centered));

        Assert.Equal(15, request.Board.OnRollBorneOffCount);
    }

    [Theory]
    [InlineData(ApiCubeOwner.Centered, DiagramCubeOwner.Centered)]
    [InlineData(ApiCubeOwner.SeatOne, DiagramCubeOwner.OnRoll)]     // seat One = the positive side
    [InlineData(ApiCubeOwner.SeatTwo, DiagramCubeOwner.Opponent)]
    public void CubeOwner_MapsSeatKeyedOntoTheFixedFrame(ApiCubeOwner apiOwner, DiagramCubeOwner expected)
    {
        DiagramRequest request = ReplayDiagramMapper.ForFinalState(
            Context(), Position(cubeValue: 2, apiOwner));

        Assert.Equal(expected, request.Display!.CubeOwner);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MatchBoard_StatesTheGamesCrawfordFlagAsServed(bool isCrawford)
    {
        var entry = new PlayEntry(Seat.One, Position(), Die1: 2, Die2: 1, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(
            Context(matchLength: 7, seatOneScore: 6, seatTwoScore: 3, isCrawford), entry);

        Assert.Equal(new MatchRailScore(onRollNeeds: 1, opponentNeeds: 4, isCrawford), request.Display!.Score);
    }

    [Fact]
    public void MoneySession_StatesTheMoneyLabelWithNoJacobyRule()
    {
        // The API's match length 0 is a money session, and the API states no
        // Jacoby rule, so the board states none: MoneyRailScore(null), never a
        // guessed true or false, and no away scores.
        var entry = new PlayEntry(Seat.One, Position(), Die1: 4, Die2: 2, Moves: []);

        DiagramRequest request = ReplayDiagramMapper.ForEntry(
            Context(matchLength: 0, seatOneScore: 5, seatTwoScore: 3), entry);

        Assert.Equal(new MoneyRailScore(isJacoby: null), request.Display!.Score);
    }

    [Fact]
    public void MalformedBoard_IsRefusedAsAnArgumentException()
    {
        // The viewers' fail-visible boundary catches ArgumentException: a
        // served board that breaks BoardPosition's invariant (here, sixteen
        // checkers of seat One's) is refused with one.
        int[] sixteenCheckers = [.. OpeningBoard];
        sixteenCheckers[6] = 6;

        Assert.ThrowsAny<ArgumentException>(() => ReplayDiagramMapper.ForFinalState(
            Context(), new GamePosition(sixteenCheckers, 1, ApiCubeOwner.Centered)));
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
}
