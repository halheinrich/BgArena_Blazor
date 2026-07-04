using BgArena_Blazor.Components;
using BgArena_Blazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The UI host talks to the tournament server server-to-server (no CORS);
// every page reaches it through this one typed client.
builder.Services.AddHttpClient<ArenaClient>(client =>
    client.BaseAddress = new Uri(
        builder.Configuration["Arena:BaseAddress"]
            ?? throw new InvalidOperationException("Missing required configuration 'Arena:BaseAddress'.")));

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

app.Run();
