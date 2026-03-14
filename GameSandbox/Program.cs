// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.GameObjects;
using GameEngineTools.FileSystem;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;
using static GameEngineTools.Characters.Engines.ActionNames;
using GameEngineTools.Extensions;

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

var significantOther = manager.NPPCs.Where(npc => npc is NPC && npc.Person.Biology != player.Person.Biology && (Math.Abs(npc.Person.Identity.BirthDate.Year - clock.Now.Year) >= 16 || Math.Abs(npc.Person.Identity.BirthDate.Year - player.Person.Identity.BirthDate.Year) <= 5)).FirstOrDefault();

if (significantOther is null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    throw new InvalidOperationException(nameof(significantOther));
}

var playerPerson = player.Person;
var significantOtherPerson = significantOther.Person;

var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters = [playerPerson, significantOtherPerson],
    SimulationYears = 2,
    TickStep = WTimeSpan.FromHours(0.5),
    ClockAdvance = WTimeSpan.FromHours(1),

    OnTick = (now, chars) =>
    {
        var p = chars[0];
        var so = chars[1];

        // Naplánované akce
        if (now.Day is 2 or 6 or 12)
            so.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(30), p.Id, so.Id, SpeechAct.SmallTalk, "Ahoooj"));

        if (now.Day is 13)
            so.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(12), p.Id, so.Id, SpeechAct.Humor, "Vtip"));

        if (now.Day is 16)
            so.ReceiveEvent(new MicroPositive(
                now + WTimeSpan.FromMinutes(20), so.Id, p.Id, "Smile"));

        if (now.Day is 16 && now.Hour is 20
            && p.Snapshot.InteractionSurface.Location != "Castle"
            && so.Snapshot.InteractionSurface.Location != "Castle")
        {
            p.ReceiveEvent(new ContextChanged(
                now + WTimeSpan.FromHours(1), p.Id, "Castle", true, 0.13, 0.2));
            so.ReceiveEvent(new ContextChanged(
                now + WTimeSpan.FromHours(1), so.Id, "Castle", true, 0.13, 0.2));
        }

        if (now.Day is 10)
            p.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(12), so.Id, p.Id, SpeechAct.Validation, "Sluší ti to."));

        // Reach out routing
        foreach (var character in chars)
        {
            var reachOut = character.LastOutbox.OfType<ActionCommitted>().FirstOrDefault(a => a.ActionName == ReachOut);

            if (reachOut == null)
                continue;

            var target = chars.FirstOrDefault(c => c.Id != character.Id && c.Snapshot.InteractionSurface.Location == character.Snapshot.InteractionSurface.Location);

            target?.ReceiveEvent(new InteractionProposed(now, character.Id, target.Id, SpeechAct.SmallTalk, null));
        }

        // ── Sleep handling ────────────────────────────────────────────────────────
        // Žádný handler → auto pro všechny postavy (výchozí chování scény).
        // Odkomentuj až budeš mít UI:
        //
        // SleepPromptHandlers = new Dictionary<HumanId, Func<SleepPromptRequested, bool>>
        // {
        //     [playerPerson.Id] = _ =>
        //     {
        //         Console.WriteLine("[SPÁNEK] Jít spát? (A/n)");
        //         return Console.ReadKey(true).Key != ConsoleKey.N;
        //     }
        // }
    }
});

await scene.RunAsync();

await File.WriteAllTextAsync(gameTimePath, clock.Now.WorldTicks.ToString());
gf.Export(player);
gf.Export((NPC)significantOther);

var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
await File.WriteAllTextAsync(Path.Combine(desktopPath, $"player.{playerPerson.Id.Value.ToString()}.txt"), player.PrintInfo(false));
await File.WriteAllTextAsync(Path.Combine(desktopPath, $"significantOther.{significantOtherPerson.Id.Value.ToString()}.txt"), significantOther.PrintInfo(false));

Console.WriteLine("Simulace dokončena. Herní čas: {0}", clock.Now);
