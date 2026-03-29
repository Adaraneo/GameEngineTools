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
using GameEngineTools.World.Location;
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
    consoleLogs: true,
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

var locationService = new DefaultLocationService();

locationService.RegisterLocation(new LocationDescriptor(
    Id: "village_square",
    DisplayName: "Village Square",
    BaseNoise: 0.3,
    NoisePerPerson: 0.05,
    Capacity: 20,
    AllowsPrivacy: false,
    LocationType.Social));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_hall",
    DisplayName: "Castle Hall",
    BaseNoise: 0.1,
    NoisePerPerson: 0.02,
    Capacity: 10,
    AllowsPrivacy: true,
    LocationType.Private));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_sleep_room",
    DisplayName: "Castle Sleep Room",
    BaseNoise: 0.1,
    NoisePerPerson: 0.1,
    Capacity: 10,
    AllowsPrivacy: true,
    LocationType.Rest));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "village_house_sleep_room",
    DisplayName: "Village House Sleep Room",
    BaseNoise: 0.2,
    NoisePerPerson: 0.2,
    Capacity: 2,
    AllowsPrivacy: true,
    LocationType.Rest));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_horse_stables",
    DisplayName: "Castle Horse Stables",
    BaseNoise: 0.3,
    NoisePerPerson: 0.2,
    Capacity: 10,
    AllowsPrivacy: false,
    LocationType.Work));

if (locationService.GetLocation(playerPerson.Id) is null && locationService.GetLocation(significantOtherPerson.Id) is null)
{
    locationService.MoveCharacter(playerPerson.Id, "village_square");
    locationService.MoveCharacter(significantOtherPerson.Id, "village_square");
}

foreach (var npc in manager.Characters.Where(npc => npc.Person.Id != significantOtherPerson.Id && npc.Person.Id != playerPerson.Id))
{
    if (locationService.GetLocation(npc.Person.Id) is null)
    {
        locationService.MoveCharacter(npc.Person.Id, "village_square");
    }
}

var characters = new List<IHuman> { playerPerson, significantOtherPerson };
characters.AddRange(manager.Characters.Where(c => c.Person.Id != playerPerson.Id && c.Person.Id != significantOtherPerson.Id).Select(c => c.Person).ToList());

var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters = characters,
    LocationService = locationService,
    SimulationYears = 2,
    TickStep = WTimeSpan.FromHours(0.5),
    NarrativeFormatter = new DefaultNarrativeFormatter(),

    ResolveCharacter = id =>
    {
        var chars = characters.ToArray();
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
        // ── First impressions — all unmet pairs sharing a location ────────────
        // Replaces the old hardcoded p/so pair check.
        FireFirstImpressions(now, chars, attractionCalculator, locationService);

        // ── NPC movement — route MoveTo:* actions from previous tick ─────────
        RouteMoveTo(now, chars, locationService, rng);

        // ── Location context — move both to Castle on day 16, evening ─────────
        if (now.Day is 16 && now.Hour is 20 && locationService.GetLocation(significantOtherPerson.Id).ToLowerInvariant() != "castle_hall" && locationService.GetLocation(playerPerson.Id).ToLowerInvariant() != "castle_hall")
        {
            locationService.MoveCharacter(playerPerson.Id, "castle_hall");
            locationService.MoveCharacter(significantOtherPerson.Id, "castle_hall");
        }

        // ── ReachOut routing — dynamic, relationship-aware ────────────────────
        foreach (var character in chars)
        {
            var reachOut = character.LastOutbox
                .OfType<ActionCommitted>()
                .FirstOrDefault(a => a.ActionName == ReachOut);

            if (reachOut is null)
                continue;

            // Ask the location service who is in the same location right now.
            var locationId = locationService.GetLocation(character.Id);
            if (locationId is null)
                continue;

            var candidates = locationService
                .GetCharactersAt(locationId)
                .Where(id => id != character.Id)
                .Select(id => chars.FirstOrDefault(c => c.Id == id))
                .OfType<IHuman>()
                .ToList();

            if (candidates.Count == 0)
                continue;

            // Weighted random — prefer characters the initiator likes,
            // but keep a chance to approach a stranger.
            // Unknown character gets neutral weight 45 — openness to strangers.
            var target = PickWeightedRandom(candidates, c =>
            {
                var edge = character.Snapshot.Relationships.Edges.GetValueOrDefault(c.Id);
                return edge?.Like ?? 45.0;
            }, rng);

            var targetEdge = character.Snapshot.Relationships.Edges.GetValueOrDefault(target.Id);
            var act = ChooseSpeechAct(targetEdge, rng);

            target.ReceiveEvent(new InteractionProposed(now, character.Id, target.Id, act, null));
            TryTouch(now, character, target, rng);
        }

        // ── Organic MicroPositive — witnessing effort ─────────────────────────
        foreach (var character in chars)
        {
            var justCreated = character.LastOutbox
                .OfType<ActionCommitted>()
                .Any(a => a.ActionName is Create or Work);

            if (!justCreated)
                continue;

            var locationId = locationService.GetLocation(character.Id);
            if (locationId is null)
                continue;

            var witnesses = locationService
                .GetCharactersAt(locationId)
                .Where(id => id != character.Id)
                .Select(id => chars.FirstOrDefault(c => c.Id == id))
                .OfType<IHuman>()
                .ToList();

            if (witnesses.Count == 0)
                continue;

            // Pick one random witness — only one MicroPositive per creative action.
            var witness = witnesses[rng.Next(witnesses.Count)];

            if (rng.NextDouble() < 0.30)
                character.ReceiveEvent(new MicroPositive(now, witness.Id, character.Id, "noticed your work"));
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
var others = manager.Characters.Where(npc => !npc.Equals(significantOther) && !npc.Equals(player)).ToList();
foreach (var other in others)
{
    gf.Export((NPC)other);
}

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

# region Helper Methods

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

/// <summary>
/// Selects one element from <paramref name="candidates"/> using weighted random sampling.
/// Higher weight means higher probability of being selected.
/// Falls back to uniform random when all weights are zero.
/// </summary>
/// <typeparam name="T">Element type.</typeparam>
/// <param name="candidates">Non-empty list of candidates.</param>
/// <param name="weight">Weight function — must return a non-negative value.</param>
/// <param name="rng">Random source.</param>
static T PickWeightedRandom<T>(IReadOnlyList<T> candidates, Func<T, double> weight, Random rng)
{
    var totalWeight = candidates.Sum(weight);

    // Uniform fallback when all weights are zero (e.g. all strangers with Like=0)
    if (totalWeight <= 0)
        return candidates[rng.Next(candidates.Count)];

    var threshold = rng.NextDouble() * totalWeight;
    var accumulated = 0.0;

    foreach (var candidate in candidates)
    {
        accumulated += weight(candidate);
        if (accumulated >= threshold)
            return candidate;
    }

    // Floating-point safety net — return last element
    return candidates[^1];
}

/// <summary>
/// Fires <see cref="FirstImpressionFormed"/> for every pair of characters
/// that share a location and have not yet met (no relationship edge exists).
/// Each pair is processed exactly once — A→B and B→A in a single pass.
/// </summary>
/// <param name="now">Current simulation time.</param>
/// <param name="chars">All characters in the scene.</param>
/// <param name="calculator">Shared attraction calculator singleton.</param>
/// <param name="locations">Location service — used to group characters by place.</param>
static void FireFirstImpressions(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    IAttractionCalculator calculator,
    ILocationService locations)
{
    // Build a lookup: HumanId → IHuman for O(1) resolve inside the loop.
    var byId = chars.ToDictionary(c => c.Id);

    // Get all registered location ids that have at least one character.
    var occupiedLocations = chars
        .Select(c => locations.GetLocation(c.Id))
        .Where(loc => loc is not null)
        .Distinct()!;

    foreach (var locationId in occupiedLocations)
    {
        var ids = locations.GetCharactersAt(locationId);

        // Iterate every unique pair (i, j) — no duplicates, no self-pairs.
        for (var i = 0; i < ids.Count; i++)
            for (var j = i + 1; j < ids.Count; j++)
            {
                if (!byId.TryGetValue(ids[i], out var a)) continue;
                if (!byId.TryGetValue(ids[j], out var b)) continue;

                // Skip pairs that already have a relationship edge — they have already met.
                if (a.Snapshot.Relationships.Edges.ContainsKey(b.Id))
                    continue;

                var viewA = AppearanceProjector.Compute(a.PhysicalAppearance, a.Snapshot.Physiology, a.Biology);
                var viewB = AppearanceProjector.Compute(b.PhysicalAppearance, b.Snapshot.Physiology, b.Biology);

                // A sees B
                var aResult = a.AttractionProfile is not null
                    ? calculator.Calculate(a.AttractionProfile, b.PhysicalAppearance, viewB, b.Biology,
                                           observerValence: a.Snapshot.Psychology.Valence)
                    : AttractionResult.Neutral;

                // B sees A
                var bResult = b.AttractionProfile is not null
                    ? calculator.Calculate(b.AttractionProfile, a.PhysicalAppearance, viewA, a.Biology,
                                           observerValence: b.Snapshot.Psychology.Valence)
                    : AttractionResult.Neutral;

                a.ReceiveEvent(new FirstImpressionFormed(now, a.Id, b.Id,
                    aResult.FirstImpressionLike, aResult.Score,
                    aResult.BasePhysical, aResult.PreferenceMatch));
                b.ReceiveEvent(new FirstImpressionFormed(now, b.Id, a.Id,
                    bResult.FirstImpressionLike, bResult.Score,
                    bResult.BasePhysical, bResult.PreferenceMatch));
            }
    }
}

/// <summary>
/// Routes <c>MoveTo:*</c> actions emitted by <see cref="DefaultBehaviorEngine"/>
/// to a concrete location via <see cref="ILocationService"/>.
/// </summary>
/// <remarks>
/// The engine emits the intent (e.g. <c>"MoveTo:Social"</c>); this method
/// resolves a concrete location of the requested type, avoiding the current one
/// and preferring locations that are not overcrowded (Crowding &lt; 0.8).
/// </remarks>
static void RouteMoveTo(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    ILocationService locations,
    Random rng)
{
    foreach (var character in chars)
    {
        var moveTo = character.LastOutbox
            .OfType<ActionCommitted>()
            .FirstOrDefault(a => a.ActionName.StartsWith("MoveTo:"));

        if (moveTo is null)
            continue;

        // Parse the requested LocationType from action name suffix
        var typeName = moveTo.ActionName["MoveTo:".Length..];
        if (!Enum.TryParse<LocationType>(typeName, out var requestedType))
            continue;

        var currentLocation = locations.GetLocation(character.Id);

        // Find candidate locations of the requested type,
        // excluding the current one and overcrowded ones.
        var candidates = locations
            .GetLocationsByType(requestedType)
            .Where(id => id != currentLocation)
            .ToList();

        if (candidates.Count == 0)
        {
            Console.WriteLine($"[MoveTo] {character.Id.Value} requested {requestedType}, but no alternative location exists.");
            continue;
        }

        // Pick randomly — could be weighted by crowding in the future
        var chosen = candidates[rng.Next(candidates.Count)];
        locations.MoveCharacter(character.Id, chosen);
    }
}

#endregion