using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Components;

namespace BgArena_Blazor.Components.Pages;

/// <summary>
/// The tournament dashboard: a create form whose participant list is an
/// ordered pick (order is seeding order — the final standings tie-break)
/// plus every tournament record, newest first, polling-refreshed. Validation
/// is server-authoritative, matching the match launch form.
/// </summary>
public partial class Tournaments
{
    /// <summary>The typed admin client.</summary>
    [Inject]
    public ArenaClient Arena { get; set; } = default!;

    /// <summary>Navigation, for jumping to the created tournament's detail page.</summary>
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    /// <summary>Latest engine listing feeding the participant picker; null until first load.</summary>
    protected IReadOnlyList<EngineSummary>? EngineRows { get; private set; }

    /// <summary>Latest tournament rows, newest first; null until first load.</summary>
    protected IReadOnlyList<TournamentSummary>? TournamentRows { get; private set; }

    /// <summary>The picker's current selection; empty when nothing is picked.</summary>
    protected string SelectedEngine { get; set; } = string.Empty;

    /// <summary>The chosen participants in seeding order.</summary>
    protected List<string> Participants { get; } = [];

    /// <summary>The create form's remaining state.</summary>
    protected CreateFormModel Form { get; } = new();

    /// <summary>The server's refusal reason for the last create attempt, if any.</summary>
    protected string? CreateError { get; private set; }

    /// <summary>Engines not yet added to the participant list.</summary>
    protected IEnumerable<EngineSummary> AvailableEngines =>
        (EngineRows ?? []).Where(engine => !Participants.Contains(engine.Name));

    /// <inheritdoc />
    protected override async Task RefreshAsync(CancellationToken cancellationToken)
    {
        EngineRows = await Arena.GetEnginesAsync(cancellationToken);
        // The server serves creation order; the dashboard reads newest first.
        TournamentRows = [.. (await Arena.GetTournamentsAsync(cancellationToken)).Reverse()];
    }

    /// <summary>Appends the picked engine to the seeding order.</summary>
    protected void AddParticipant()
    {
        if (SelectedEngine.Length == 0 || Participants.Contains(SelectedEngine))
            return;
        Participants.Add(SelectedEngine);
        SelectedEngine = string.Empty;
    }

    /// <summary>Moves a participant one seeding slot up.</summary>
    protected void MoveUp(int index)
    {
        if (index <= 0)
            return;
        (Participants[index - 1], Participants[index]) = (Participants[index], Participants[index - 1]);
    }

    /// <summary>Moves a participant one seeding slot down.</summary>
    protected void MoveDown(int index)
    {
        if (index >= Participants.Count - 1)
            return;
        (Participants[index + 1], Participants[index]) = (Participants[index], Participants[index + 1]);
    }

    /// <summary>Removes a participant from the seeding order.</summary>
    protected void RemoveParticipant(int index) => Participants.RemoveAt(index);

    /// <summary>Submits the create form; navigates to the new tournament on success.</summary>
    protected async Task CreateAsync()
    {
        ArenaResult<TournamentSummary> result = await Arena.StartTournamentAsync(
            new StartTournamentRequest([.. Participants], Form.MatchLength, Form.MatchesPerPairing, Form.Seed),
            PollCancellation);
        if (result.IsSuccess)
        {
            CreateError = null;
            Navigation.NavigateTo($"tournaments/{result.Value.TournamentId}");
        }
        else
        {
            CreateError = result.Error ?? $"The server refused the tournament ({(int)result.StatusCode}).";
        }
    }

    /// <summary>Mutable backing state for the create form's inputs.</summary>
    protected sealed class CreateFormModel
    {
        /// <summary>Match length in points for every scheduled match (≥ 1).</summary>
        public int MatchLength { get; set; } = 5;

        /// <summary>How many times each pair meets; an even count balances the opening-roll seat.</summary>
        public int MatchesPerPairing { get; set; } = 2;

        /// <summary>Optional tournament seed; server-chosen and recorded when omitted.</summary>
        public int? Seed { get; set; }
    }
}
