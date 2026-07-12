extern alias TournamentServer;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BgArena_Blazor.Tests;

/// <summary>
/// A tournament-server factory that journals into an isolated per-instance
/// directory under the system temp area, deleted on dispose. Without it the
/// in-proc server takes its default <c>Persistence:DataDirectory</c> of
/// <c>data</c>, resolved against the server project's content root — so it
/// writes journals into <c>BgTournament.Server/data/</c> and rehydrates them
/// on the next boot, leaking terminal records across runs and across tests
/// (listing pollution the id-specific asserts would otherwise miss). Mirrors
/// the producer's own ServerHarness isolation.
///
/// <para>An optional <paramref name="settings"/> layer is applied over the host
/// configuration after the data-directory isolation — e.g. <c>Admin:ApiKeys</c>
/// to boot an <em>enforcing</em> server for the admin-auth wire tests. The
/// isolation always applies; the settings are additive.</para>
///
/// <para>Naming the server's <c>Program</c> through the <c>TournamentServer</c>
/// extern alias is confined to this one file, so consumers reference the
/// factory by type and never trip the two-<c>Program</c> CS0433 hazard.</para>
/// </summary>
internal sealed class IsolatedTournamentServer : WebApplicationFactory<TournamentServer::Program>
{
    private readonly string _dataDirectory =
        Directory.CreateTempSubdirectory("bgarena-server-").FullName;

    private readonly IReadOnlyDictionary<string, string>? _settings;

    /// <param name="settings">
    /// Extra host settings layered over the data-directory isolation (e.g.
    /// <c>["Admin:ApiKeys:director"] = "director-secret"</c>). Null for the
    /// unconfigured, anonymously-serving default the smoke boots.
    /// </param>
    public IsolatedTournamentServer(IReadOnlyDictionary<string, string>? settings = null) =>
        _settings = settings;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Persistence:DataDirectory", _dataDirectory);
        if (_settings is not null)
        {
            foreach ((string key, string value) in _settings)
                builder.UseSetting(key, value);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Directory.Delete(_dataDirectory, recursive: true);
            }
            catch (DirectoryNotFoundException)
            {
                // Never written to (e.g. a test that started no match): fine.
            }
        }
    }
}
