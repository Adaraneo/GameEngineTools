// Program.cs
// Copyright (c) 50PSoftware

using WorldObserver.Hubs;
using WorldObserver.Simulation;

// The engine resolves its config and SourceFiles relative to Directory.GetCurrentDirectory().
// Under IIS (w3wp) the process working directory is NOT the app folder, so pin it to the deployment
// directory where appsettings*.json + SourceFiles\ live. Harmless when run as a console app from bin.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// ── Connotation layer + lexical acquisition ─────────────────────────────────────────────────
// Both are configured in WorldBootstrap rather than here, because the ambient interpreter now also
// takes the per-character vocabulary store — and that only exists once the runtime's DI container is
// built. Configuring here as well would win by ordering and silently hand the engines an interpreter
// with no vocabulary, so word choice would colour emotions but nobody would ever fail to understand
// a word. To fall back to pre-connotation behaviour, flip the flag in WorldBootstrap.

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddSignalR()
    .AddJsonProtocol(o =>
        o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
builder.Services.Configure<WorldObserverOptions>(builder.Configuration.GetSection("WorldObserver"));
builder.Services.AddSingleton(sp =>
{
    var opt = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<WorldObserverOptions>>().Value;
    return new SimulationControl(opt.DefaultDelayMs, opt.TickStepMinutes);
});
builder.Services.AddSingleton<CharacterPort>();
builder.Services.AddHostedService<WorldHostedService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<WorldHub>("/world");

// ── Character export / import (the browser picks the folder via the File System Access API) ──
app.MapGet("/api/characters/export", (CharacterPort port) =>
    port.Ready ? Results.Json(port.Export()) : Results.StatusCode(503));

app.MapPost("/api/characters/import", (CharacterPort port, List<WorldObserver.Dtos.CharacterFileDto> files, bool replace = false, long worldTimeTicks = 0) =>
    Results.Json(new { accepted = port.QueueImport(files ?? new List<WorldObserver.Dtos.CharacterFileDto>(), replace, worldTimeTicks) }));

app.Run();
