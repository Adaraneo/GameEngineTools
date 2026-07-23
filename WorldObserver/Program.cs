// Program.cs
// Copyright (c) 50PSoftware

using WorldObserver.Hubs;
using WorldObserver.Simulation;

// The engine resolves its config and SourceFiles relative to Directory.GetCurrentDirectory().
// Under IIS (w3wp) the process working directory is NOT the app folder, so pin it to the deployment
// directory where appsettings*.json + SourceFiles\ live. Harmless when run as a console app from bin.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// ── Connotation-layer live experiment (Phase-2 gate evaluation) ─────────────────────────────
// WorldObserver is the experiment surface: enable the opt-in connotation layer with the curated
// lexicon so word choice (chválit vs souhlasit, …) colours listeners' emotions and hostile
// readings sting more. Engines read the ambient SpeechActInterpretation.Current, so Psychology
// and Memory interpret with the same configuration. Remove/flip to fall back to byte-identical
// pre-connotation behaviour.
GameEngineTools.Dialogue.Interpretation.SpeechActInterpretation.Configure(
    new GameEngineTools.Dialogue.Interpretation.SpeechActInterpreterConfig(EnableConnotationLayer: true),
    new GameEngineTools.Dialogue.Semantics.CuratedConnotationLexicon());

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
