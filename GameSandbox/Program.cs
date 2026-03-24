// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Traits;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.Narrative;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using static GameEngineTools.Characters.Engines.ActionNames;
using NPC = GameEngineTools.Characters.GameObjects.NPC;
using TFSC = GameEngineTools.Constants.TestFSConstatns;

// ── Game time ─────────────────────────────────────────────────────────────────
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
var attractionCalculator = (DefaultAttractionCalculator)runtime.Services.GetRequiredService<IAttractionCalculator>();

// ── Characters ────────────────────────────────────────────────────────────────
var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.Characters.Add(player);

clock.SetNow(initTicks == defaultTicks
    ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(16))
    : new WDateTime(initTicks));

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
    manager.Characters.Add(gf.ImportNPC(new FileInfo(filename).Name));

// Find a significant other: opposite biology, close in age (within 5 years), at least 16
var significantOther = manager.Characters
    .Where(npc => npc is NPC
        && npc.Person.Biology != player.Person.Biology
        && (npc.Age >= 16
        || Math.Abs(npc.Age - player.Age) <= 5))
    .FirstOrDefault();

if (significantOther is null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    throw new InvalidOperationException(nameof(significantOther));
}

var playerPerson = player.Person;
var significantOtherPerson = significantOther.Person;

var diary = new List<NarrativeEntry>();

// Shared random source for OnTick scene logic.
// IHuman does not expose IRandomSource — that lives on IHumanContext (engine-internal).
// System.Random is sufficient here: scene-level decisions are not deterministic anyway.
var rng = new Random();

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
            Console.WriteLine($"* [{entry.OccurredAt}] {entry.Text}");
            Console.ResetColor();
        }
        else if (entry.Priority == NarrativePriority.Medium)
        {
            Console.WriteLine($"  [{entry.OccurredAt}] {entry.Text}");
        }
    },

    OnTick = (now, chars) =>
    {
        var p = chars[0];
        var so = chars[1];

        // ── First impression — computed from attraction profile, fired once ────
        // Only when the relationship edge does not yet exist.
        if (!p.Snapshot.Relationships.Edges.ContainsKey(so.Id))
        {
            var soView = AppearanceProjector.Compute(so.PhysicalAppearance, so.Snapshot.Physiology, so.Biology);
            var pView = AppearanceProjector.Compute(p.PhysicalAppearance, p.Snapshot.Physiology, p.Biology);

            var pResult = p.AttractionProfile is not null
                ? attractionCalculator.Calculate(p.AttractionProfile, so.PhysicalAppearance, soView, so.Biology, observerValence: p.Snapshot.Psychology.Valence)
                : AttractionResult.Neutral;

            var soResult = so.AttractionProfile is not null
                ? attractionCalculator.Calculate(so.AttractionProfile, p.PhysicalAppearance, pView, p.Biology, observerValence: so.Snapshot.Psychology.Valence)
                : AttractionResult.Neutral;

            p.ReceiveEvent(new FirstImpressionFormed(now, p.Id, so.Id, pResult.FirstImpressionLike, pResult.Score));
            so.ReceiveEvent(new FirstImpressionFormed(now, so.Id, p.Id, soResult.FirstImpressionLike, soResult.Score));
        }

        // ── Location context — move both to Castle on day 16, evening ─────────
        // This is a narrative beat: a shared environment that enables intimacy.
        // HasPrivacy=true, low noise, low crowding.
        if (now.Day is 16 && now.Hour is 20
            && p.Snapshot.InteractionSurface.Location != "Castle"
            && so.Snapshot.InteractionSurface.Location != "Castle")
        {
            p.ReceiveEvent(new ContextChanged(now + WTimeSpan.FromHours(1), p.Id, "Castle", true, 0.13, 0.2));
            so.ReceiveEvent(new ContextChanged(now + WTimeSpan.FromHours(1), so.Id, "Castle", true, 0.13, 0.2));
        }

        // ── ReachOut routing — dynamic, relationship-aware ────────────────────
        // When a character decides to ReachOut, we translate that into a concrete
        // InteractionProposed. The SpeechAct is chosen based on how close the
        // characters already are — shallow early on, deeper as trust grows.
        foreach (var character in chars)
        {
            var reachOut = character.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == ReachOut);

            if (reachOut is null)
                continue;

            var target = chars.FirstOrDefault(c =>
                c.Id != character.Id &&
                c.Snapshot.InteractionSurface.Location == character.Snapshot.InteractionSurface.Location);

            if (target is null)
                continue;

            var edge = character.Snapshot.Relationships.Edges.GetValueOrDefault(target.Id);
            var act = ChooseSpeechAct(edge, rng);

            target.ReceiveEvent(new InteractionProposed(now, character.Id, target.Id, act, null));

            // Physical contact attempt — only when emotionally close enough.
            // Probability is intentionally low to model the rarity of these moments.
            TryTouch(now, character, target, rng);
        }

        // ── Organic MicroPositive — witnessing effort ─────────────────────────
        // When a character finishes a creative or productive action and the other
        // is in the same location, there is a small chance of a spontaneous
        // positive micro-interaction (noticing, encouraging).
        foreach (var character in chars)
        {
            var justCreated = character.LastOutbox
                .OfType<ActionCommitted>()
                .Any(a => a.ActionName is Create or Work);

            if (!justCreated)
                continue;

            var witness = chars.FirstOrDefault(c =>
                c.Id != character.Id &&
                c.Snapshot.InteractionSurface.Location == character.Snapshot.InteractionSurface.Location);

            // 30% chance: witness notices and reacts positively
            if (witness is not null && rng.NextDouble() < 0.30)
            {
                character.ReceiveEvent(new MicroPositive(now, witness.Id, character.Id, "noticed your work"));
            }
        }
    }
});

await scene.RunAsync();

// ── Diary export ──────────────────────────────────────────────────────────────
var sbDiary = new StringBuilder();
AddDiaryEntry(sbDiary, $"\n=== DIARY ({diary.Count} entries) ===");

foreach (var entry in diary.OrderBy(e => e.OccurredAt))
{
    var prefix = entry.Priority switch
    {
        NarrativePriority.High => "* ",
        NarrativePriority.Medium => ". ",
        _ => "  "
    };
    AddDiaryEntry(sbDiary, $"{prefix}[{entry.OccurredAt}] {entry.Text}");
}

await File.WriteAllTextAsync(gameTimePath, clock.Now.WorldTicks.ToString());
gf.Export(player);
gf.Export((NPC)significantOther);

var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

await File.WriteAllTextAsync(
    Path.Combine(desktopPath, $"player.{playerPerson.Id.Value}.txt"),
    player.PrintInfo(false));

await File.WriteAllTextAsync(
    Path.Combine(desktopPath, $"significantOther.{significantOtherPerson.Id.Value}.txt"),
    significantOther.PrintInfo(false));

Console.WriteLine("Simulation complete. Game time: {0}", clock.Now);

File.WriteAllText(
    Path.Combine(desktopPath, $"diary.{clock.Now.Date}.txt"),
    sbDiary.ToString());

Console.ReadKey();

// ── Helper methods ────────────────────────────────────────────────────────────

/// <summary>
/// Chooses a <see cref="SpeechAct"/> appropriate for the current relationship depth.
/// </summary>
/// <remarks>
/// The progression mirrors real-world social dynamics:
/// strangers exchange small talk, acquaintances begin asking questions,
/// close friends risk self-disclosure and vulnerability.
/// </remarks>
/// <param name="edge">Current relationship edge, or <c>null</c> if characters have not met.</param>
/// <param name="rng">Random source from the initiating character's context.</param>
/// <returns>The most contextually appropriate <see cref="SpeechAct"/>.</returns>
static SpeechAct ChooseSpeechAct(RelationshipEdge? edge, Random rng)
{
    // Strangers or very early acquaintance — safe, low-risk opening
    if (edge is null || edge.Closeness < 20)
        return SpeechAct.SmallTalk;

    // Getting to know each other — curiosity starts showing
    if (edge.Closeness < 40)
        return rng.NextDouble() < 0.40 ? SpeechAct.Question : SpeechAct.SmallTalk;

    // Established acquaintance — humor and deeper curiosity become natural
    if (edge.Closeness < 60)
    {
        if (rng.NextDouble() < 0.30) return SpeechAct.SelfDisclosure;
        if (rng.NextDouble() < 0.40) return SpeechAct.Humor;
        return SpeechAct.Question;
    }

    // Close relationship — validation, meta-commentary, vulnerability
    if (rng.NextDouble() < 0.25) return SpeechAct.Validation;
    if (rng.NextDouble() < 0.35) return SpeechAct.SelfDisclosure;
    return SpeechAct.Meta;
}

/// <summary>
/// Attempts a physical touch interaction if the relationship conditions are met.
/// </summary>
/// <remarks>
/// Touch is gated on Closeness, Comfort, and Attraction to prevent unrealistic
/// physical contact between characters who have not built the necessary trust.
/// Privacy context further modulates the probability of intimate touch.
/// </remarks>
/// <param name="now">Current game time.</param>
/// <param name="from">Initiating character.</param>
/// <param name="to">Receiving character.</param>
/// <param name="rng">Random source from the initiating character's context.</param>
static void TryTouch(WDateTime now, IHuman from, IHuman to, Random rng)
{
    var edge = from.Snapshot.Relationships.Edges.GetValueOrDefault(to.Id);
    if (edge is null)
        return;

    var hasPrivacy = from.Snapshot.InteractionSurface.HasPrivacy;

    // Light touch — shoulder, arm. Requires moderate closeness and comfort.
    // 12% base chance keeps it rare enough to feel meaningful.
    if (edge.Closeness > 50 && edge.Comfort > 45 && rng.NextDouble() < 0.12)
    {
        to.ReceiveEvent(new TouchAttempted(now, from.Id, to.Id, TouchLevel.Light));
        return; // One touch attempt per tick is enough
    }

    // Friendly touch — hug or equivalent. Requires deeper closeness, more attraction,
    // and privacy — open spaces make this socially awkward.
    if (edge.Closeness > 70 && edge.Attraction > 55 && hasPrivacy && rng.NextDouble() < 0.07)
    {
        to.ReceiveEvent(new TouchAttempted(now, from.Id, to.Id, TouchLevel.Friendly));
    }
}

static void AddDiaryEntry(StringBuilder stringBuilder, string entry)
{
    Console.WriteLine(entry);
    stringBuilder.AppendLine(entry);
}
