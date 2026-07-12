using System.Net;
using System.Text;
using BgArena_Blazor.Services;
using BgTournament.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BgArena_Blazor.Tests;

/// <summary>
/// The admin API-key adaptation end to end. Two layers, no stubbed contract:
/// <list type="bullet">
/// <item>the typed-client registration (Program.cs) attaches the key — read
/// from <c>Arena:ApiKey</c> — as a default header on <em>every</em> outgoing
/// request, in the header <see cref="AdminApiKey.HeaderName"/> owns, and omits
/// it entirely when unconfigured (today's byte-identical anonymous shape);</item>
/// <item>against the real server booted enforcing (keys configured), a keyed
/// host is served while a keyless client's 401 folds cleanly through
/// <see cref="ArenaResult{T}"/> — the producer's fail-loud config-mismatch
/// contract, proven over the real wire.</item>
/// </list>
/// The header is captured off the outgoing request (the transport-stub seam the
/// client tests use), never inferred; the enforcing legs run the real
/// identity gate in-proc.
/// </summary>
public class ArenaClientAuthTests
{
    private const string DirectorSecret = "director-secret";

    /// <summary>The enforcing-server configuration: one named key, "director".</summary>
    private static readonly IReadOnlyDictionary<string, string> DirectorKey =
        new Dictionary<string, string> { ["Admin:ApiKeys:director"] = DirectorSecret };

    /// <summary>
    /// A transport that answers every call with an empty JSON list and keeps the
    /// outgoing request, so a test can read the headers the registration put on
    /// the wire without a live server.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>
    /// Boots the real Arena host with the given extra settings and the given
    /// primary transport for its one typed client, so the registration under
    /// test (Program.cs) runs verbatim — only the socket is swapped.
    /// </summary>
    private static WebApplicationFactory<Program> ArenaHost(
        Func<HttpMessageHandler> primaryHandler,
        IReadOnlyDictionary<string, string>? settings = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            if (settings is not null)
            {
                foreach ((string key, string value) in settings)
                    builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
                services.AddHttpClient<ArenaClient>()
                    .ConfigurePrimaryHttpMessageHandler(primaryHandler));
        });

    // ---- registration: the header rides every request when configured -------

    [Fact]
    public async Task Registration_ApiKeyConfigured_PresentsHeaderOnEveryRequest()
    {
        var capturing = new CapturingHandler();
        using WebApplicationFactory<Program> host = ArenaHost(
            () => capturing,
            settings: new Dictionary<string, string> { ["Arena:ApiKey"] = DirectorSecret });

        ArenaClient arena = host.Services.GetRequiredService<ArenaClient>();
        // Two distinct endpoints off the one typed client — the header is a
        // property of the client, not of any single call site.
        await arena.GetEnginesAsync();
        await arena.GetMatchesAsync();

        Assert.Equal(2, capturing.Requests.Count);
        Assert.All(capturing.Requests, request =>
        {
            Assert.True(request.Headers.TryGetValues(AdminApiKey.HeaderName, out IEnumerable<string>? values));
            Assert.Equal(DirectorSecret, Assert.Single(values!));
        });
    }

    [Fact]
    public async Task Registration_NoApiKey_PresentsNoHeader()
    {
        var capturing = new CapturingHandler();
        using WebApplicationFactory<Program> host = ArenaHost(() => capturing);

        ArenaClient arena = host.Services.GetRequiredService<ArenaClient>();
        await arena.GetEnginesAsync();

        HttpRequestMessage request = Assert.Single(capturing.Requests);
        Assert.False(request.Headers.Contains(AdminApiKey.HeaderName));
    }

    // ---- the real enforcing server ------------------------------------------

    [Fact]
    public async Task Enforcing_ConfiguredHostIsServed_FullChainOverTheRealWire()
    {
        // The whole deployment path: Program.cs reads Arena:ApiKey, attaches the
        // header, and the real identity gate validates it and serves — with the
        // Arena host's typed client pointed at the enforcing server in-proc.
        using var server = new IsolatedTournamentServer(DirectorKey);
        using WebApplicationFactory<Program> host = ArenaHost(
            () => server.Server.CreateHandler(),
            settings: new Dictionary<string, string> { ["Arena:ApiKey"] = DirectorSecret });

        ArenaClient arena = host.Services.GetRequiredService<ArenaClient>();
        IReadOnlyList<MatchSummary> matches = await arena.GetMatchesAsync();
        Assert.Empty(matches);
    }

    [Fact]
    public async Task Enforcing_KeylessClient_401FoldsCleanlyThroughArenaResult()
    {
        using var server = new IsolatedTournamentServer(DirectorKey);
        var arena = new ArenaClient(server.CreateClient());   // no key presented

        // The auth gate short-circuits ahead of any engine validation, so the
        // unknown-engine request is refused for its missing key, not its engines.
        ArenaResult<MatchSummary> result = await arena.StartMatchAsync(
            new StartMatchRequest("Alpha", "Beta", MatchLength: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Contains("requires an admin API key", result.Error);
        // The reason is safe to surface: it never echoes a key value.
        Assert.DoesNotContain(DirectorSecret, result.Error);
    }
}
