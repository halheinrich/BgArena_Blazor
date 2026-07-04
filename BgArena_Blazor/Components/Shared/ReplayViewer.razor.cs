using BackgammonDiagram_Lib;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Shared;

/// <summary>
/// Steps through a completed match game by game, entry by entry: a game
/// picker, a position stepper whose last step is the game's
/// <c>finalState</c>, an actor-and-action caption per step, and the board.
/// The viewer only walks served positions — moves are printed verbatim in
/// the actor's own notation and never applied to a board app-side. A
/// position the diagram cannot draw (Builder validation) renders a visible
/// error in place of the board while stepping stays available.
/// </summary>
public partial class ReplayViewer
{
    private string? _forMatchId;
    private int _gameIndex;

    /// <summary>The completed match's replay payload.</summary>
    [Parameter]
    [EditorRequired]
    public MatchGamesResponse Replay { get; set; } = default!;

    /// <summary>The current step: an index into the game's entries, or one past the last (the final state).</summary>
    protected int Cursor { get; private set; }

    /// <summary>The mapped request for the current step; null when mapping failed.</summary>
    protected DiagramRequest? CurrentRequest { get; private set; }

    /// <summary>Why the current step cannot be rendered, when it cannot.</summary>
    protected string? MappingError { get; private set; }

    /// <summary>The selected game; setting it jumps to that game's first step.</summary>
    protected int GameIndex
    {
        get => _gameIndex;
        set
        {
            if (_gameIndex == value)
                return;
            _gameIndex = value;
            Cursor = 0;
            UpdateCurrent();
        }
    }

    /// <summary>The cursor value that shows the final state.</summary>
    protected int MaxCursor => CurrentGame.Entries.Count;

    /// <summary>The caption for the current step: who did what, or the game outcome.</summary>
    protected string Caption => Cursor < MaxCursor
        ? EntryCaption(CurrentGame.Entries[Cursor])
        : FinalCaption(CurrentGame);

    private GameReplay CurrentGame => Replay.Games[_gameIndex];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A new payload (different match) resets the walk; re-passing the
        // same match's payload preserves the current position.
        if (_forMatchId == Replay.MatchId)
            return;
        _forMatchId = Replay.MatchId;
        _gameIndex = 0;
        Cursor = 0;
        if (Replay.Games.Count > 0)
            UpdateCurrent();
    }

    /// <summary>Steps one position back.</summary>
    protected void StepBack() => MoveCursor(Cursor - 1);

    /// <summary>Steps one position forward.</summary>
    protected void StepForward() => MoveCursor(Cursor + 1);

    /// <summary>Jumps to the game's first position.</summary>
    protected void JumpToStart() => MoveCursor(0);

    /// <summary>Jumps to the game's final position.</summary>
    protected void JumpToEnd() => MoveCursor(MaxCursor);

    /// <summary>One line describing a game in the picker.</summary>
    protected string GameLabel(GameReplay game) =>
        $"Game {game.GameNumber} — {EngineName(game.Winner)} +{game.Points} · {game.SeatOneScore}–{game.SeatTwoScore}"
        + (game.IsCrawford ? " · Crawford" : string.Empty);

    private void MoveCursor(int cursor)
    {
        if (cursor == Cursor || cursor < 0 || cursor > MaxCursor)
            return;
        Cursor = cursor;
        UpdateCurrent();
    }

    private void UpdateCurrent()
    {
        GameReplay game = CurrentGame;
        DiagramContext context = DiagramContext.ForGame(Replay, game);
        try
        {
            CurrentRequest = Cursor < game.Entries.Count
                ? ReplayDiagramMapper.ForEntry(context, game.Entries[Cursor])
                : ReplayDiagramMapper.ForFinalState(context, game.FinalState);
            MappingError = null;
        }
        catch (InvalidOperationException exception)
        {
            // Builder validation refused the position — e.g. a cube beyond
            // the renderer's 4096 cap, which the producer does not cap. Fail
            // visible instead of clamping or crashing; stepping stays alive.
            CurrentRequest = null;
            MappingError = exception.Message;
        }
    }

    private string EntryCaption(GameEntry entry) => entry switch
    {
        PlayEntry play =>
            $"{EngineName(play.Actor)} rolls {play.Die1}-{play.Die2}: {MovesText(play.Moves)}",
        CubeOfferEntry offer =>
            $"{EngineName(offer.Actor)} doubles to {offer.State.CubeValue * 2}",
        CubeResponseEntry { Action: CubeResponseAction.Take } response =>
            $"{EngineName(response.Actor)} takes",
        CubeResponseEntry response =>
            $"{EngineName(response.Actor)} passes",
        _ => throw new InvalidOperationException($"Unknown replay entry kind '{entry.GetType().Name}'."),
    };

    private string FinalCaption(GameReplay game) =>
        $"Game {game.GameNumber} over — {EngineName(game.Winner)} wins {game.Points} "
        + (game.Points == 1 ? "point" : "points")
        + $" ({game.ResultKind.ToString().ToLowerInvariant()})";

    private string EngineName(Seat seat) => seat == Seat.One ? Replay.EngineOne : Replay.EngineTwo;

    /// <summary>
    /// Moves stay in the actor's own numbering, printed verbatim — never
    /// interpreted. The contract's two sentinels get their standard notation
    /// names: from 25 enters off the actor's bar, to 0 bears the checker off.
    /// </summary>
    private static string MovesText(IReadOnlyList<PlayMove> moves) =>
        moves.Count == 0
            ? "no play"
            : string.Join(" ", moves.Select(move =>
                $"{(move.From == 25 ? "bar" : move.From.ToString())}/{(move.To == 0 ? "off" : move.To.ToString())}"));
}
