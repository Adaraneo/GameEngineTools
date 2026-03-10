// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Utils.Time;
using GameSandbox.Scenes;
using Microsoft.Extensions.DependencyInjection;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

// ── Herní čas ─────────────────────────────────────────────────────────────────
var gameTimePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    "GameTime.txt");

var spec = GameEngineToolsRuntime.LoadSpec();
var defaultTicks = spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;

var initTicks = File.Exists(gameTimePath) && long.TryParse(File.ReadAllText(gameTimePath), out var saved)
    ? saved
    : defaultTicks;

// ── Runtime ───────────────────────────────────────────────────────────────────
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    new WDateTime(initTicks),
    consoleLogs: false,
    generatedFileOptions: new GeneratedFileOptions
    {
        PlayerDirectory = TFSC.player,
        NPCDirectory = TFSC.NPCs
    });

var gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;
var clock = (SystemClock)runtime.Clock;

// ── Postavy ───────────────────────────────────────────────────────────────────
var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.NPPCs.Add(player);

clock.SetNow(initTicks == defaultTicks
    ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(16))
    : new WDateTime(initTicks));

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
    manager.NPPCs.Add(gf.ImportNPC(new FileInfo(filename).Name));

var significantOther = manager.NPPCs.First(x => x is NPC);

// ── Scéna ─────────────────────────────────────────────────────────────────────
// Jediné místo kde definuješ CO se simuluje — scéna se stará o HOW.
var scene = new InteractionScene(runtime, gameTimePath, new InteractionSceneOptions
{
    Player = player,
    Npc = significantOther,
    SimulationYears = 2,
    TickStep = WTimeSpan.FromHours(0.5),
    ClockAdvance = WTimeSpan.FromHours(1),

    // Scénář: sem napíšeš jen co chceš testovat — zbytek řeší scéna
    OnTick = (now, p, npc) =>
    {
        // Dny 2, 6, 12 → hráč zahajuje small talk s NPC
        if (now.Day is 2 or 6 or 12)
            npc.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(30), p.Id, npc.Id, SpeechAct.SmallTalk, "Ahoooj"));

        if (now.Day is 13)
            npc.ReceiveEvent(new InteractionProposed(now + WTimeSpan.FromMinutes(12), p.Id, npc.Id, SpeechAct.Humor, "Vtip"));

        if (now.Day is 16)
            npc.ReceiveEvent(new MicroPositive(now + WTimeSpan.FromMinutes(20), npc.Id, p.Id, "Whatever..."));

        if (now.Day is 16 && now.Hour is 20 && p.Snapshot.InteractionSurface.Location != "Castle" && npc.Snapshot.InteractionSurface.Location != "Castle")
        {
            p.ReceiveEvent(new ContextChanged(now + WTimeSpan.FromHours(1), p.Id, "Castle", true, 0.13, 0.2));
            npc.ReceiveEvent(new ContextChanged(now + WTimeSpan.FromHours(1), npc.Id, "Castle", true, 0.13, 0.2));
        }

        // Den 10 → NPC posílá hráči validaci
        if (now.Day is 10)
            p.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(12), npc.Id, p.Id, SpeechAct.Validation, "Sluší ti to."));
    },

    // Sleep: Auto = potvrdí automaticky. Až budeš mít UI, přepni na Manual:
    PlayerSleepHandling = SleepHandling.Auto,
    // OnSleepPrompt = _ =>
    // {
    //     Console.WriteLine("[SPÁNEK] Jít spát? (A/N)");
    //     return Console.ReadKey(true).Key == ConsoleKey.A;
    // }
});

await scene.RunAsync();
