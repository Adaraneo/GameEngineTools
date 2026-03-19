// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.GameObjects;
using GameEngineTools.Characters.Traits;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.Narrative;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text;
using static GameEngineTools.Characters.Engines.ActionNames;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

// ── Herní čas ─────────────────────────────────────────────────────────────────
var gameTimePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
    "gametime.bin");

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
manager.Characters.Add(player);

clock.SetNow(initTicks == defaultTicks
    ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(16))
    : new WDateTime(initTicks));

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
    manager.Characters.Add(gf.ImportNPC(new FileInfo(filename).Name));

var significantOther = manager.Characters.Where(npc => npc is NPC && npc.Person.Biology != player.Person.Biology && (Math.Abs(npc.Person.Identity.BirthDate.Year - clock.Now.Year) >= 16 || Math.Abs(npc.Person.Identity.BirthDate.Year - player.Person.Identity.BirthDate.Year) <= 5)).FirstOrDefault();

if (significantOther is null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    throw new InvalidOperationException(nameof(significantOther));
}

var playerPerson = player.Person;
var significantOtherPerson = significantOther.Person;

static double ComputeAttraction(AppearanceView view)
{
    // PostureScore = jak se postava drží (únava, bolest)
    // AcneLevel    = stav pleti (imunitní zátěž)
    // Bloating     = napnutí (cyklus)
    var attraction = 50.0;
    attraction += (view.PostureScore - 50) * 0.3;   // dobrá postava → +, shrbená → -
    attraction -= view.AcneLevel * 0.15;             // špatná pleť → mírně dolů
    attraction -= (int)view.Bloating * 3.0;          // None=0, Light=-3, Medium=-6, High=-9

    return Math.Clamp(attraction, 0, 100);
}

var diary = new List<NarrativeEntry>();

var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters = [playerPerson, significantOtherPerson],
    SimulationYears = 2,
    TickStep = WTimeSpan.FromHours(0.5),
    NarrativeFormatter = new DefaultNarrativeFormatter(),

    ResolveCharacter = id =>
    {
        var chars = new[] { playerPerson, significantOtherPerson };
        var found = chars.FirstOrDefault(c => c.Id == id);

        return found is not null
        ? new NarrativeCharacterInfo(found.Identity.FirstName.Original, found.Biology)
        : new NarrativeCharacterInfo(id.Value.ToString()[..8], GameEngineTools.Characters.Core.SexBiology.Unknown);
    },

    OnNarrative = entry =>
    {
        diary.Add(entry);

        if (entry.Priority == NarrativePriority.High)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"* [{entry.OccurredAt.ToString()}] {entry.Text}");
            Console.ResetColor();
        }
        else if (entry.Priority == NarrativePriority.Medium)
        {
            Console.WriteLine($"  [{entry.OccurredAt.ToString()}] {entry.Text}");
        }
    },

    OnTick = (now, chars) =>
    {
        var p = chars[0];
        var so = chars[1];

        if (!p.Snapshot.Relationships.Edges.ContainsKey(so.Id))
        {
            var soView = AppearanceProjector.Compute(so.PhysicalAppearance, so.Snapshot.Physiology, so.Biology);
            var pView = AppearanceProjector.Compute(p.PhysicalAppearance, p.Snapshot.Physiology, p.Biology);

            p.ReceiveEvent(new FirstImpressionFormed(now, p.Id, so.Id, 0, ComputeAttraction(soView)));
            so.ReceiveEvent(new FirstImpressionFormed(now, so.Id, p.Id, 0, ComputeAttraction(pView)));
        }

        // Naplánované akce
        if (now.Day is 2 or 6 or 12 && now.Hour is 8)
            so.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(30), p.Id, so.Id, SpeechAct.SmallTalk, "Ahoooj"));

        if (now.Day is 13 && now.Hour is 13)
            so.ReceiveEvent(new InteractionProposed(
                now + WTimeSpan.FromMinutes(12), p.Id, so.Id, SpeechAct.Humor, "Vtip"));

        if (now.Day is 16 && now.Hour is 12)
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

        // Light touch — až když jsou dostatečně blízko
        if (now.Day is 20 && now.Hour == 15)
        {
            var edge = p.Snapshot.Relationships.Edges.GetValueOrDefault(so.Id);
            if (edge?.Closeness > 30)
                so.ReceiveEvent(new TouchAttempted(
                    now + WTimeSpan.FromMinutes(15), p.Id, so.Id, TouchLevel.Light));
        }

        if (now.Day is 10 && now.Hour == 16)
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

// Zápis do deníku
var sbDiary = new StringBuilder();
AddDiaryEntry(sbDiary, $"\n=== DENÍK ({diary.Count} záznamů) ===");
foreach (var entry in diary.OrderBy(e => e.OccurredAt))
{
    var prefix = entry.Priority switch
    {
        NarrativePriority.High => "* ",
        NarrativePriority.Medium => ". ",
        _ => "  "
    };
    AddDiaryEntry(sbDiary, $"{prefix}[{entry.OccurredAt.ToString()}] {entry.Text}");
}

await File.WriteAllTextAsync(gameTimePath, clock.Now.WorldTicks.ToString());
gf.Export(player);
gf.Export((NPC)significantOther);

var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
await File.WriteAllTextAsync(Path.Combine(desktopPath, $"player.{playerPerson.Id.Value.ToString()}.txt"), player.PrintInfo(false));
await File.WriteAllTextAsync(Path.Combine(desktopPath, $"significantOther.{significantOtherPerson.Id.Value.ToString()}.txt"), significantOther.PrintInfo(false));

Console.WriteLine("Simulace dokončena. Herní čas: {0}", clock.Now);

File.WriteAllText(Path.Combine(desktopPath, $"diary.{clock.Now.Date.ToString()}.txt"), sbDiary.ToString());

Console.ReadKey();

static void AddDiaryEntry(StringBuilder stringBuilder, string entry)
{
    Console.WriteLine(entry);
    stringBuilder.AppendLine(entry);
}