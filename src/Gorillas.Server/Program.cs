using Gorillas.Contracts;
using Gorillas.Data;
using Gorillas.Server;
using Gorillas.Server.Components;
using Gorillas.Server.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var connectionString = builder.Configuration.GetConnectionString("Gorillas")
    ?? "Data Source=gorillas.db";

builder.Services.AddGorillasData(connectionString);
builder.Services.AddGorillasRealtime(connectionString);

var app = builder.Build();

// The read model and the Akka journal share this file, so the schema must exist before the
// actor system starts serving traffic.
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GorillasDataConnection>();
    await SchemaInitializer.EnsureCreatedAsync(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<GameHub>(GameHubRoutes.Path);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
