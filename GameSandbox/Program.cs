// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Utils.Time;
using GameSandbox.Scenes;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

// ── Herní čas ─────────────────────────────────────────────────────────────────
// Soubor na ploše pamatuje, kde jsme skončili minule.
var gameTimePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    "GameTime.txt");

var spec = GameEngineToolsRuntime.LoadSpec();
var defaultTicks = spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;

var initTicks = File.Exists(gameTimePath) && long.TryParse(File.ReadAllText(gameTimePath), out var saved)
    ? saved
    : defaultTicks;

var initNow = new WDateTime(initTicks);

// ── Runtime ───────────────────────────────────────────────────────────────────
// StartAsync sestaví DI kontejner a nakonfiguruje WWorld.
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    initNow,
    consoleLogs: false,
    generatedFileOptions: new GeneratedFileOptions
    {
        PlayerDirectory = TFSC.player,
        NPCDirectory = TFSC.NPCs
    });

// ── Scéna ─────────────────────────────────────────────────────────────────────
// Veškerá logika interakcí žije v InteractionScene — tady jen scénu spustíme.
var scene = new InteractionScene(runtime, gameTimePath);
await scene.RunAsync();
