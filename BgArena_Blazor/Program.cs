using System.Net;
using BgArena_Blazor.Components;
using BgArena_Blazor.Services;
using BgTournament.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The UI host talks to the tournament server server-to-server (no CORS);
// every page reaches it through this one typed client, so configuring the
// admin API key here covers the whole client — polling, replay, audit, the
// .MAT export relay, and the SSE live feed all ride this one instance.
builder.Services.AddHttpClient<ArenaClient>(client =>
{
    client.BaseAddress = new Uri(
        builder.Configuration["Arena:BaseAddress"]
            ?? throw new InvalidOperationException("Missing required configuration 'Arena:BaseAddress'."));

    // BgTournament's admin surface takes named API keys: the server stamps the
    // key's actor name on the durable record of the actions it authorizes. When
    // one is configured we present it on every request in the header the Api
    // assembly owns (AdminApiKey.HeaderName is the SSOT — never a copied string).
    // Absent leaves today's anonymous behavior with byte-identical requests; a
    // server enforcing keys then answers a named 401. The key is a secret — it
    // lives only on the client's default headers, is never logged, and never
    // reaches an error the UI renders.
    string? apiKey = builder.Configuration["Arena:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
        client.DefaultRequestHeaders.Add(AdminApiKey.HeaderName, apiKey);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Relay for the tournament server's .MAT export. The server is deliberately
// internal — the browser reaches it only through this host — so the download
// affordance points here, and the host fetches server-to-server via ArenaClient
// and streams the file through, preserving the served content type and
// download filename. The documented refusals fold per the ArenaResult envelope:
// 404 unknown → bodyless 404, 409 still-running → its reason; an undocumented
// upstream status has already thrown inside the client.
app.MapGet("/matches/{matchId}/export.mat", async (string matchId, ArenaClient arena, CancellationToken cancellationToken) =>
{
    ArenaResult<MatchExportFile> result = await arena.ExportMatchAsync(matchId, cancellationToken);
    if (result.IsSuccess)
    {
        MatchExportFile file = result.Value;
        return Results.File(file.Content, file.ContentType, file.FileName);
    }

    return result.StatusCode switch
    {
        HttpStatusCode.NotFound => Results.NotFound(),
        HttpStatusCode.Conflict => Results.Json(
            new ErrorResponse(result.Error ?? "Match is still running."),
            statusCode: StatusCodes.Status409Conflict),
        _ => throw new InvalidOperationException(
            $"ArenaClient folded an undocumented export refusal ({(int)result.StatusCode})."),
    };
});

app.Run();
