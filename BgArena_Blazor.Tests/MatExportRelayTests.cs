using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// The Arena host's <c>.MAT</c> relay (<c>GET /matches/{id}/export.mat</c>) in
/// isolation: the real host boots in-proc (WebApplicationFactory over the app's
/// internal Program), but its <see cref="ArenaClient"/> is pointed at a scripted
/// upstream so the tournament server's every answer can be posed exactly. Proves
/// the relay folds the documented refusals per the ArenaResult envelope — a 404
/// to a bodyless 404, a 409 to the server's reason — and, on success, streams
/// the bytes through unchanged with the served content type and download
/// filename preserved. The wire loop against the real server is closed by the
/// gating smoke; this pins the relay's own behaviour.
/// </summary>
public class MatExportRelayTests
{
    /// <summary>An upstream that answers every request from a scripted responder,
    /// capturing the path the relay's client asked for.</summary>
    private sealed class StubUpstream(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Boots the Arena host with its ArenaClient rerouted to <paramref name="upstream"/>.</summary>
    private static WebApplicationFactory<Program> ArenaWith(HttpMessageHandler upstream) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddHttpClient<ArenaClient>(client => client.BaseAddress = new Uri("http://upstream.test"))
                    .ConfigurePrimaryHttpMessageHandler(() => upstream)));

    [Fact]
    public async Task Relay_UnknownMatch_FoldsUpstream404ToABodylessNotFound()
    {
        using var factory = ArenaWith(new StubUpstream(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/matches/nope/export.mat");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Relay_RunningMatch_Folds409AndRelaysTheServersReason()
    {
        const string reason = "Match 'match-1' is still running; watch it at /matches/match-1/live.";
        using var factory = ArenaWith(new StubUpstream(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent($$"""{"error":"{{reason}}"}""", Encoding.UTF8, "application/json"),
        }));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/matches/match-1/export.mat");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        ErrorResponse? error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        Assert.Equal(reason, error?.Error);
    }

    [Fact]
    public async Task Relay_TerminalMatch_StreamsTheBytesAndPreservesContentTypeAndFilename()
    {
        byte[] body = Encoding.UTF8.GetBytes("; [Site \"BgArena\"]\n; 1 point match\n Game 1\n");
        var upstream = new StubUpstream(_ =>
        {
            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/plain") { CharSet = "utf-8" };
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = "match_match-1.mat",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var factory = ArenaWith(upstream);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/matches/match-1/export.mat");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        var disposition = response.Content.Headers.ContentDisposition;
        Assert.Equal("attachment", disposition?.DispositionType);
        Assert.Contains("match_match-1.mat", $"{disposition?.FileName} {disposition?.FileNameStar}");
        Assert.Equal(body, await response.Content.ReadAsByteArrayAsync());

        // The relay fetched from the server's own export path, id woven in.
        Assert.Equal("/matches/match-1/export.mat", upstream.LastRequestUri?.AbsolutePath);
    }
}
