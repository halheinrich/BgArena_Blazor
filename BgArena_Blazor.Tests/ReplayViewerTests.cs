using BgArena_Blazor.Components.Shared;
using BgTournament.Api;
using Bunit;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the replay viewer: the game picker, the stepper
/// (entries then finalState), the actor-and-action captions with verbatim
/// mover-relative notation ("bar"/"off" for the contract's two sentinels),
/// cursor reset on game switch, and the fail-visible path for a position the
/// diagram cannot draw (cube beyond the renderer's 4096 cap).
/// </summary>
public class ReplayViewerTests : BunitContext
{
    private static readonly int[] OpeningBoard =
        [0, -2, 0, 0, 0, 0, 5, 0, 3, 0, 0, 0, -5, 5, 0, 0, 0, -3, 0, -5, 0, 0, 0, 0, 2, 0];

    private static GamePosition Pos(int cube = 1, CubeOwner owner = CubeOwner.Centered) =>
        new(OpeningBoard, cube, owner);

    /// <summary>Game 1 of the golden shape: play, offer, take, final at cube 2.</summary>
    private static GameReplay GoldenGame() =>
        new(GameNumber: 1, Seat.Two, GameResultKind.Single, CubeValue: 2, Points: 2,
            SeatOneScore: 0, SeatTwoScore: 0, IsCrawford: false,
            Entries:
            [
                new PlayEntry(Seat.One, Pos(), Die1: 3, Die2: 1,
                    Moves: [new PlayMove(8, 5), new PlayMove(6, 5)]),
                new CubeOfferEntry(Seat.Two, Pos()),
                new CubeResponseEntry(Seat.One, Pos(), CubeResponseAction.Take),
            ],
            FinalState: Pos(cube: 2, CubeOwner.SeatOne));

    /// <summary>A second game with a dance and bar/off notation moves.</summary>
    private static GameReplay NotationGame() =>
        new(GameNumber: 2, Seat.One, GameResultKind.Gammon, CubeValue: 1, Points: 2,
            SeatOneScore: 0, SeatTwoScore: 2, IsCrawford: false,
            Entries:
            [
                new PlayEntry(Seat.Two, Pos(), Die1: 5, Die2: 5, Moves: []),
                new PlayEntry(Seat.One, Pos(), Die1: 6, Die2: 1,
                    Moves: [new PlayMove(25, 19), new PlayMove(6, 0)]),
            ],
            FinalState: Pos());

    private static MatchGamesResponse TwoGameMatch() =>
        new("match-1", "Alpha", "Beta", MatchLength: 3, [GoldenGame(), NotationGame()]);

    private IRenderedComponent<ReplayViewer> RenderViewer(MatchGamesResponse? replay = null) =>
        Render<ReplayViewer>(p => p.Add(c => c.Replay, replay ?? TwoGameMatch()));

    [Fact]
    public void FirstStep_ShowsTheOpeningPlayCaptionAndRendersTheBoard()
    {
        var cut = RenderViewer();

        Assert.Equal("Alpha rolls 3-1: 8/5 6/5", cut.Find("#step-caption").TextContent);
        Assert.Contains("step 1 of 4", cut.Find("#step-indicator").TextContent);
        Assert.NotNull(cut.Find(".replay-board svg"));
    }

    [Fact]
    public void Stepping_WalksOfferResponseAndFinalOutcome()
    {
        var cut = RenderViewer();

        cut.Find("#step-next").Click();
        Assert.Equal("Beta doubles to 2", cut.Find("#step-caption").TextContent);

        cut.Find("#step-next").Click();
        Assert.Equal("Alpha takes", cut.Find("#step-caption").TextContent);

        cut.Find("#step-next").Click();
        Assert.Equal("Game 1 over — Beta wins 2 points (single)", cut.Find("#step-caption").TextContent);
        Assert.True(cut.Find("#step-next").HasAttribute("disabled"));
        Assert.NotNull(cut.Find(".replay-board svg"));

        cut.Find("#step-prev").Click();
        Assert.Equal("Alpha takes", cut.Find("#step-caption").TextContent);
    }

    [Fact]
    public void JumpButtons_GoToFirstAndFinalPositions()
    {
        var cut = RenderViewer();

        cut.Find("#step-end").Click();
        Assert.Contains("step 4 of 4", cut.Find("#step-indicator").TextContent);

        cut.Find("#step-start").Click();
        Assert.Contains("step 1 of 4", cut.Find("#step-indicator").TextContent);
    }

    [Fact]
    public void Notation_PrintsDanceAndBarOffSentinelsVerbatim()
    {
        var cut = RenderViewer();

        cut.Find("#game-pick").Change("1");
        Assert.Equal("Beta rolls 5-5: no play", cut.Find("#step-caption").TextContent);

        cut.Find("#step-next").Click();
        Assert.Equal("Alpha rolls 6-1: bar/19 6/off", cut.Find("#step-caption").TextContent);
    }

    [Fact]
    public void GamePicker_ListsEveryGameAndSwitchResetsTheCursor()
    {
        var cut = RenderViewer();

        var options = cut.FindAll("#game-pick option");
        Assert.Equal(2, options.Count);
        Assert.Contains("Game 1 — Beta +2 · 0–0", options[0].TextContent);
        Assert.Contains("Game 2 — Alpha +2 · 0–2", options[1].TextContent);

        cut.Find("#step-end").Click();
        cut.Find("#game-pick").Change("1");
        Assert.Contains("step 1 of 3", cut.Find("#step-indicator").TextContent);
    }

    [Fact]
    public void UndrawablePosition_RendersTheErrorInsteadOfTheBoardAndSteppingSurvives()
    {
        // A cube beyond the renderer's 4096 cap is legitimate producer data
        // the diagram refuses — fail visible, not clamp, not crash.
        var game = new GameReplay(1, Seat.One, GameResultKind.Single, CubeValue: 8192, Points: 8192,
            SeatOneScore: 0, SeatTwoScore: 0, IsCrawford: false,
            Entries: [new PlayEntry(Seat.One, Pos(), 3, 1, [new PlayMove(8, 5)])],
            FinalState: Pos(cube: 8192, CubeOwner.SeatTwo));
        var cut = RenderViewer(new MatchGamesResponse("m-big", "Alpha", "Beta", 0, [game]));

        Assert.NotNull(cut.Find(".replay-board svg"));

        cut.Find("#step-end").Click();
        Assert.NotNull(cut.Find("#mapping-error"));
        Assert.Empty(cut.FindAll(".replay-board"));

        cut.Find("#step-prev").Click();
        Assert.Empty(cut.FindAll("#mapping-error"));
        Assert.NotNull(cut.Find(".replay-board svg"));
    }

    [Fact]
    public void PassResponse_CaptionsAsPasses()
    {
        var game = new GameReplay(1, Seat.Two, GameResultKind.Single, CubeValue: 1, Points: 1,
            SeatOneScore: 1, SeatTwoScore: 0, IsCrawford: false,
            Entries:
            [
                new CubeOfferEntry(Seat.Two, Pos()),
                new CubeResponseEntry(Seat.One, Pos(), CubeResponseAction.Pass),
            ],
            FinalState: Pos());
        var cut = RenderViewer(new MatchGamesResponse("m-pass", "Alpha", "Beta", 3, [game]));

        cut.Find("#step-next").Click();
        Assert.Equal("Alpha passes", cut.Find("#step-caption").TextContent);
    }
}
