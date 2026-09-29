using BgArena_Blazor.Components.Shared;
using BgTournament.Api;
using Bunit;

namespace BgArena_Blazor.Tests;

/// <summary>
/// bUnit wire tests for the replay viewer: the game picker, the stepper
/// (entries then finalState), the actor-and-action captions with verbatim
/// mover-relative notation ("bar"/"off" for the contract's two sentinels),
/// cursor reset on game switch, the fail-visible path for a position the
/// diagram cannot draw (a malformed served board), and what the drawn board
/// states through the real mapper and renderer: a play step's roll and no
/// decision-domain text on a cube or final step, the game's Crawford flag,
/// double match point worded from the score, and the money label with no
/// Jacoby rule.
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
        new("match-1", "Alpha", "Beta", MatchLength: 3, MatchStatus.Completed, [GoldenGame(), NotationGame()]);

    private IRenderedComponent<ReplayViewer> RenderViewer(MatchGamesResponse? replay = null) =>
        Render<ReplayViewer>(p => p.Add(c => c.Replay, replay ?? TwoGameMatch()));

    /// <summary>Every text the drawn board's SVG shows, in document order.</summary>
    private static IReadOnlyList<string> BoardTexts(IRenderedComponent<ReplayViewer> cut) =>
        [.. cut.FindAll(".replay-board text").Select(text => text.TextContent)];

    /// <summary>A match of one one-play game, entered at the given score (match length 0 is money).</summary>
    private static MatchGamesResponse OnePlayMatch(
        int matchLength, int seatOneScore, int seatTwoScore, bool isCrawford) =>
        new("m-score", "Alpha", "Beta", matchLength, MatchStatus.Completed,
        [
            new GameReplay(1, Seat.One, GameResultKind.Single, CubeValue: 1, Points: 1,
                seatOneScore, seatTwoScore, isCrawford,
                Entries: [new PlayEntry(Seat.One, Pos(), 3, 1, [new PlayMove(8, 5)])],
                FinalState: Pos()),
        ]);

    [Fact]
    public void FirstStep_ShowsTheOpeningPlayCaptionAndRendersTheBoard()
    {
        var cut = RenderViewer();

        Assert.Equal("Alpha rolls 3-1: 8/5 6/5", cut.Find("#step-caption").TextContent);
        Assert.Contains("step 1 of 4", cut.Find("#step-indicator").TextContent);
        Assert.NotNull(cut.Find(".replay-board svg"));
    }

    [Fact]
    public void Board_DrawsThePipCountsOfTheServedBoard()
    {
        // The diagram reads the pip counts off the drawn board — nothing
        // app-side states them — so the opening position shows 167 a side.
        var cut = RenderViewer();

        Assert.Equal(2, BoardTexts(cut).Count(text => text == "Pip: 167"));
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
        // A served board that breaks BoardPosition's invariant (sixteen of
        // seat One's checkers) cannot be drawn — fail visible, not massage,
        // not crash.
        int[] sixteenCheckers = [.. OpeningBoard];
        sixteenCheckers[6] = 6;
        var game = new GameReplay(1, Seat.One, GameResultKind.Single, CubeValue: 1, Points: 1,
            SeatOneScore: 0, SeatTwoScore: 0, IsCrawford: false,
            Entries: [new PlayEntry(Seat.One, Pos(), 3, 1, [new PlayMove(8, 5)])],
            FinalState: new GamePosition(sixteenCheckers, 1, CubeOwner.Centered));
        var cut = RenderViewer(new MatchGamesResponse("m-bad", "Alpha", "Beta", 3, MatchStatus.Completed, [game]));

        Assert.NotNull(cut.Find(".replay-board svg"));

        cut.Find("#step-end").Click();
        Assert.NotNull(cut.Find("#mapping-error"));
        Assert.Empty(cut.FindAll(".replay-board"));

        cut.Find("#step-prev").Click();
        Assert.Empty(cut.FindAll("#mapping-error"));
        Assert.NotNull(cut.Find(".replay-board svg"));
    }

    [Fact]
    public void UncappedCube_IsDrawnAsServed()
    {
        // The producer does not cap the cube, and the diagram draws the value
        // it is given (a cube's limit is the domain's rule): a large cube
        // renders, never the fail-visible panel.
        var game = new GameReplay(1, Seat.One, GameResultKind.Single, CubeValue: 8192, Points: 8192,
            SeatOneScore: 0, SeatTwoScore: 0, IsCrawford: false,
            Entries: [new PlayEntry(Seat.One, Pos(), 3, 1, [new PlayMove(8, 5)])],
            FinalState: Pos(cube: 8192, CubeOwner.SeatTwo));
        var cut = RenderViewer(new MatchGamesResponse("m-big", "Alpha", "Beta", 0, MatchStatus.Completed, [game]));

        cut.Find("#step-end").Click();

        Assert.Empty(cut.FindAll("#mapping-error"));
        Assert.Contains("8192", BoardTexts(cut));
    }

    [Fact]
    public void PlayStep_DrawsItsRoll_AndCubeAndFinalSteps_DrawNoDecisionText()
    {
        // "Cube Action?" is decision-only (Hal, 2026-09-28) and an arena board
        // is never a decision; a cube or final step states nothing in its
        // place — no title, no other text (Hal, 2026-09-29) — so its title
        // strip is empty.
        var cut = RenderViewer();
        Assert.Contains("3-1 to play", BoardTexts(cut));

        foreach (string caption in new[] { "Beta doubles to 2", "Alpha takes", "Game 1 over" })
        {
            cut.Find("#step-next").Click();
            Assert.StartsWith(caption, cut.Find("#step-caption").TextContent, StringComparison.Ordinal);
            IReadOnlyList<string> texts = BoardTexts(cut);
            Assert.DoesNotContain("Cube Action?", texts);
            Assert.DoesNotContain(texts, text => text.Contains("to play", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void CrawfordGame_DrawsTheServedFlagOnTheRailsAndTheCube()
    {
        var cut = RenderViewer(OnePlayMatch(matchLength: 3, seatOneScore: 2, seatTwoScore: 0, isCrawford: true));

        IReadOnlyList<string> texts = BoardTexts(cut);
        Assert.Contains("Alpha needs 1 Crawford", texts);
        Assert.Contains("Beta needs 3 Crawford", texts);
        Assert.Contains("Cr", texts);
    }

    [Fact]
    public void GameServedAsNotCrawford_DrawsNoCrawfordAtTheSameScore()
    {
        // The same score served as not the Crawford game (post-Crawford): the
        // flag is the API's, never re-derived from the score.
        var cut = RenderViewer(OnePlayMatch(matchLength: 3, seatOneScore: 2, seatTwoScore: 0, isCrawford: false));

        IReadOnlyList<string> texts = BoardTexts(cut);
        Assert.Contains("Alpha needs 1", texts);
        Assert.Contains("64", texts);
        Assert.DoesNotContain(texts, text => text.Contains("Crawford", StringComparison.Ordinal));
        Assert.DoesNotContain("Cr", texts);
    }

    [Fact]
    public void DoubleMatchPoint_IsWordedFromTheScore()
    {
        // 1-away/1-away: the diagram words Dmp from the away scores — DMP is
        // not a supplied flag (Hal, 2026-09-28).
        var cut = RenderViewer(OnePlayMatch(matchLength: 3, seatOneScore: 2, seatTwoScore: 2, isCrawford: false));

        IReadOnlyList<string> texts = BoardTexts(cut);
        Assert.Contains("Alpha needs 1", texts);
        Assert.Contains("Beta needs 1", texts);
        Assert.Contains("Dmp", texts);
    }

    [Fact]
    public void MoneySession_DrawsTheMoneyLabelWithNoJacobyRule()
    {
        // The API states no Jacoby rule, so the board states none: the bare
        // money label on both rails, never a guessed rule.
        var cut = RenderViewer(OnePlayMatch(matchLength: 0, seatOneScore: 0, seatTwoScore: 0, isCrawford: false));

        IReadOnlyList<string> texts = BoardTexts(cut);
        Assert.Contains("Alpha (Money Game)", texts);
        Assert.Contains("Beta (Money Game)", texts);
        Assert.DoesNotContain(texts, text => text.Contains("Jacoby", StringComparison.Ordinal));
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
        var cut = RenderViewer(new MatchGamesResponse("m-pass", "Alpha", "Beta", 3, MatchStatus.Completed, [game]));

        cut.Find("#step-next").Click();
        Assert.Equal("Alpha passes", cut.Find("#step-caption").TextContent);
    }
}
