using System.Net;
using System.Text;
using BgArena_Blazor.Services;

namespace BgArena_Blazor.Tests;

/// <summary>
/// Transport-layer stub for page tests: canned JSON per "METHOD /path" route,
/// so a page's ArenaClient runs its real request/deserialization path. An
/// unmapped route throws (a test that hits an unexpected endpoint fails
/// loudly instead of hanging on a fake 404). Set
/// <see cref="ThrowConnectionError"/> to simulate an unreachable server.
/// </summary>
internal sealed class RoutedJsonHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body, string ContentType)> _routes = [];

    /// <summary>The body of the most recent POST, for request-shape asserts.</summary>
    public string? LastPostBody { get; private set; }

    /// <summary>When true every request throws <see cref="HttpRequestException"/>.</summary>
    public bool ThrowConnectionError { get; set; }

    /// <summary>Maps a route key like <c>"GET /engines"</c> to a canned JSON response.</summary>
    public RoutedJsonHandler Map(string methodAndPath, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[methodAndPath] = (status, json, "application/json");
        return this;
    }

    /// <summary>
    /// Maps a route to a canned <c>text/event-stream</c> body — the live feed's
    /// transport, so the page's SSE parse path runs against real framing.
    /// </summary>
    public RoutedJsonHandler MapEventStream(string methodAndPath, string sseBody)
    {
        _routes[methodAndPath] = (HttpStatusCode.OK, sseBody, "text/event-stream");
        return this;
    }

    /// <summary>Builds an <see cref="ArenaClient"/> whose transport is this handler.</summary>
    public ArenaClient ToClient() =>
        new(new HttpClient(this) { BaseAddress = new Uri("http://arena.test") });

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ThrowConnectionError)
            throw new HttpRequestException("Connection refused (stubbed).");

        if (request.Method == HttpMethod.Post && request.Content is not null)
            LastPostBody = await request.Content.ReadAsStringAsync(cancellationToken);

        string key = $"{request.Method} {request.RequestUri!.AbsolutePath}";
        if (!_routes.TryGetValue(key, out (HttpStatusCode Status, string Body, string ContentType) route))
            throw new InvalidOperationException($"No stubbed route for '{key}'.");

        return new HttpResponseMessage(route.Status)
        {
            Content = new StringContent(route.Body, Encoding.UTF8, route.ContentType),
        };
    }
}
