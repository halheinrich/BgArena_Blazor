using System.Net;
using System.Net.Http.Json;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BgTournament.Api;

namespace BgArena_Blazor.Services;

/// <summary>
/// Typed HTTP client over the tournament server's admin API — the app's only
/// route to the server. Shapes come from <c>BgTournament.Api</c>; JSON uses
/// plain Web defaults with zero converter configuration (the producer
/// contract — enum strings and polymorphic replay entries deserialize as-is).
/// Contract-documented refusals surface as <see cref="ArenaResult{T}"/>;
/// anything the contract does not document throws.
/// </summary>
public sealed class ArenaClient
{
    private readonly HttpClient _http;

    /// <summary>
    /// Creates the client over an <see cref="HttpClient"/> whose
    /// <see cref="HttpClient.BaseAddress"/> points at the tournament server
    /// (configuration key <c>Arena:BaseAddress</c>).
    /// </summary>
    public ArenaClient(HttpClient http) => _http = http;

    /// <summary>Lists the connected engines (<c>GET /engines</c>).</summary>
    public async Task<IReadOnlyList<EngineSummary>> GetEnginesAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<IReadOnlyList<EngineSummary>>("/engines", cancellationToken)
            ?? throw new JsonException("GET /engines returned a null listing.");

    /// <summary>Lists every match record in creation order (<c>GET /matches</c>).</summary>
    public async Task<IReadOnlyList<MatchSummary>> GetMatchesAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<IReadOnlyList<MatchSummary>>("/matches", cancellationToken)
            ?? throw new JsonException("GET /matches returned a null listing.");

    /// <summary>Lists every tournament record in creation order (<c>GET /tournaments</c>).</summary>
    public async Task<IReadOnlyList<TournamentSummary>> GetTournamentsAsync(CancellationToken cancellationToken = default) =>
        await _http.GetFromJsonAsync<IReadOnlyList<TournamentSummary>>("/tournaments", cancellationToken)
            ?? throw new JsonException("GET /tournaments returned a null listing.");

    /// <summary>
    /// Fetches one match record (<c>GET /matches/{matchId}</c>).
    /// Documented refusal: 404 (unknown id, no body).
    /// </summary>
    public async Task<ArenaResult<MatchSummary>> GetMatchAsync(string matchId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"/matches/{Uri.EscapeDataString(matchId)}", cancellationToken);
        return await ToResultAsync<MatchSummary>(response, cancellationToken, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Fetches one tournament record (<c>GET /tournaments/{tournamentId}</c>).
    /// Documented refusal: 404 (unknown id, no body).
    /// </summary>
    public async Task<ArenaResult<TournamentSummary>> GetTournamentAsync(string tournamentId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"/tournaments/{Uri.EscapeDataString(tournamentId)}", cancellationToken);
        return await ToResultAsync<TournamentSummary>(response, cancellationToken, HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Fetches a completed match's replay (<c>GET /matches/{matchId}/games</c>).
    /// Documented refusals: 404 (unknown id, no body); 409 with a reason (the
    /// match is known but not <see cref="MatchStatus.Completed"/> — running
    /// matches have no settled transcripts yet, forfeited/aborted/faulted
    /// matches retain none).
    /// </summary>
    public async Task<ArenaResult<MatchGamesResponse>> GetMatchGamesAsync(string matchId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync($"/matches/{Uri.EscapeDataString(matchId)}/games", cancellationToken);
        return await ToResultAsync<MatchGamesResponse>(response, cancellationToken,
            HttpStatusCode.NotFound, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Starts a standalone match between two connected engines
    /// (<c>POST /matches</c>). Documented refusals: 400 (invalid request),
    /// 404 (unknown engine), 409 (engine busy / same engine twice) — each with
    /// a reason.
    /// </summary>
    public async Task<ArenaResult<MatchSummary>> StartMatchAsync(StartMatchRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("/matches", request, cancellationToken);
        return await ToResultAsync<MatchSummary>(response, cancellationToken,
            HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Starts a round-robin tournament among connected engines
    /// (<c>POST /tournaments</c>). Documented refusals: 400 (invalid request),
    /// 404 (unknown engine), 409 (engine busy) — each with a reason.
    /// </summary>
    public async Task<ArenaResult<TournamentSummary>> StartTournamentAsync(StartTournamentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("/tournaments", request, cancellationToken);
        return await ToResultAsync<TournamentSummary>(response, cancellationToken,
            HttpStatusCode.BadRequest, HttpStatusCode.NotFound, HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Subscribes to a match's live per-move feed (<c>GET /matches/{matchId}/live</c>,
    /// an <c>text/event-stream</c>). The refusal decision is made at subscribe
    /// time from the response status, so the documented 404 (unknown id, no
    /// body) folds into <see cref="ArenaResult{T}"/> exactly like every other
    /// by-id GET; a successful subscription carries the ordered event stream
    /// inside <see cref="ArenaResult{T}.Value"/>.
    /// <para>The house error split holds over the stream: a transport drop
    /// mid-feed surfaces to the caller as it enumerates (the page re-subscribes;
    /// the fresh snapshot re-establishes state — the v1 contract has no
    /// <c>Last-Event-ID</c> resume), while a malformed event payload throws
    /// <see cref="JsonException"/> (a producer contract break, fail loud).
    /// Enumerating the returned stream owns the underlying response's lifetime;
    /// disposing the enumerator (or cancelling) closes the connection.</para>
    /// </summary>
    public async Task<ArenaResult<IAsyncEnumerable<LiveMatchEvent>>> SubscribeMatchLiveAsync(
        string matchId, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/matches/{Uri.EscapeDataString(matchId)}/live");
        HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            response.Dispose();
            return ArenaResult<IAsyncEnumerable<LiveMatchEvent>>.Refused(HttpStatusCode.NotFound, error: null);
        }

        if (!response.IsSuccessStatusCode)
        {
            // Undocumented status: fail loud, and don't leak the response.
            try { response.EnsureSuccessStatusCode(); }
            finally { response.Dispose(); }
        }

        return ArenaResult<IAsyncEnumerable<LiveMatchEvent>>.Ok(StreamLiveAsync(response, cancellationToken));
    }

    /// <summary>
    /// Drains the SSE body into the ordered event sequence, taking ownership of
    /// the response so it (and the connection) is disposed when enumeration
    /// ends — whether the terminal event arrives, the caller stops early, or
    /// the token cancels.
    /// </summary>
    private static async IAsyncEnumerable<LiveMatchEvent> StreamLiveAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using (response)
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            SseParser<LiveMatchEvent> parser = SseParser.Create<LiveMatchEvent>(stream, ParseLiveEvent);
            await foreach (SseItem<LiveMatchEvent> item in parser.EnumerateAsync(cancellationToken))
                yield return item.Data;
        }
    }

    /// <summary>
    /// Deserializes one SSE event's <c>data</c> payload to the discriminated
    /// <see cref="LiveMatchEvent"/> union with plain Web defaults — the SSE
    /// framing (event type) carries nothing load-bearing. A null payload is a
    /// contract break.
    /// </summary>
    private static LiveMatchEvent ParseLiveEvent(string eventType, ReadOnlySpan<byte> data) =>
        JsonSerializer.Deserialize<LiveMatchEvent>(data, JsonSerializerOptions.Web)
            ?? throw new JsonException("Live feed sent a null event payload.");

    /// <summary>
    /// Folds a response into an <see cref="ArenaResult{T}"/>: success
    /// deserializes the payload; a documented refusal carries the status and
    /// the <see cref="ErrorResponse"/> reason when a body is present; any
    /// undocumented status throws <see cref="HttpRequestException"/>.
    /// </summary>
    private static async Task<ArenaResult<T>> ToResultAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        params HttpStatusCode[] documentedRefusals)
    {
        if (response.IsSuccessStatusCode)
        {
            T payload = await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new JsonException($"{response.RequestMessage?.RequestUri} returned a null payload.");
            return ArenaResult<T>.Ok(payload);
        }

        if (!documentedRefusals.Contains(response.StatusCode))
            response.EnsureSuccessStatusCode();

        // Read as text first: a bodyless documented refusal (404 from a by-id
        // GET) has no ErrorResponse to parse, and this stays correct even when
        // the transport omits Content-Length.
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        string? error = string.IsNullOrWhiteSpace(body)
            ? null
            : JsonSerializer.Deserialize<ErrorResponse>(body, JsonSerializerOptions.Web)?.Error;
        return ArenaResult<T>.Refused(response.StatusCode, error);
    }
}
