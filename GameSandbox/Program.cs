// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Schedule;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Generation.Portraits;
using GameEngineTools.Characters.Hosting;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.Narrative;
using GameEngineTools.Universe;
using GameEngineTools.World.Core.Astro;
using GameEngineTools.World.Location;
using GameEngineTools.World.Movement;
using GameEngineTools.World.Objects;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Text;
using static GameEngineTools.Characters.Engines.ActionNames;
using AppearanceProjector = GameEngineTools.Characters.Traits.AppearanceProjector;
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
    writeTextLogs: false,
    generatedFileOptions: new GeneratedFileOptions
    {
        PlayerDirectory = TFSC.player,
        NPCDirectory = TFSC.NPCs
    });

var gf = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;
var clock = (SystemClock)runtime.Clock;
var attractionCalculator = (DefaultAttractionCalculator)runtime.Services.GetRequiredService<IAttractionCalculator>();
var lodRuntime = runtime.Services.GetRequiredService<ICognitiveResolutionLevelRuntime>();
var perceptionPolicy = runtime.Services.GetRequiredService<IPerceptionFidelityPolicy>();

// ── Characters ────────────────────────────────────────────────────────────────
var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.Characters.Add(player);

var startNow = initTicks == defaultTicks ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(14)) : new WDateTime(initTicks);

clock.SetNow(startNow);

Console.Title = startNow.Date.ToString();

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
    manager.Characters.Add(gf.ImportNPC(new FileInfo(filename).Name));

// Register all imported characters in FamilyGraph so that kin queries
// work from the first tick. FamilyBuilder.Wire() calls Register() internally
// for freshly generated families; for loaded characters we must do it manually.
var familyGraph = runtime.Services.GetRequiredService<FamilyGraph>();
foreach (var character in manager.Characters)
{
    familyGraph.Register(character.Person);
}

#region input settings

Console.Write("Would you like to export prompts after simulations is complete? [y\\N] \b");
bool canGeneratePrompts = false;
var answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canGeneratePrompts = true;

Console.Clear();
Console.Write("Would you like to export player's and other main character's info after simulation? [y\\N] \b");
bool canExportMainCharactersInfo = false;
answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canExportMainCharactersInfo = true;

Console.Clear();
Console.Write("Would you like to export diary after simulation? [y\\N] \b");
bool canExportDiary = false;
answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canExportDiary = true;

long simulationDays = 20;

SetDaysForSimulation(ref simulationDays);

static void SetDaysForSimulation(ref long simulationDays, bool printInfo = true)
{
    Console.Clear();
    Console.Write("Set days for simulation: ");
    var answer = Console.ReadLine();
    var parsed = long.TryParse(answer, out simulationDays);
    if (answer.Length == 0 && !parsed)
    {
        simulationDays = 20;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("The simulation days are not set or are in incorrect format.");
        Console.ResetColor();
        Console.WriteLine("Would you like to try it again? [y\\N]");
        if (printInfo)
        {
            Console.WriteLine("If you answer no (n), simulation days will be set to 20.");
        }

        var answerKey = Console.ReadKey().Key;
        if (answerKey == ConsoleKey.Y)
        {
            SetDaysForSimulation(ref simulationDays, false);
        }
    }

    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.Write("Info: ");
    Console.ResetColor();
    Console.WriteLine("Simulation days: {0}", simulationDays);
}

#endregion input settings

var currDir = Directory.GetCurrentDirectory();

var configProvider = new ConfigurationBuilder().SetBasePath(currDir).AddJsonFile("appsettings.World.json").Build();
var perceptionOptions = configProvider.GetSection("World:Perception").Get<CharacterPerceptionOptions>() ?? new CharacterPerceptionOptions();
var astroOptions = configProvider.GetSection("World:Astro").Get<AstroConfig>() ?? new AstroConfig();
var universeOptions = configProvider.GetSection("World:Universe").Get<UniverseConfig>() ?? new UniverseConfig();

// ── World habitability ────────────────────────────────────────────────────────
{
    var star = universeOptions.ToStarPhysics();
    var orbit = universeOptions.ToOrbitalElements();
    var planet = universeOptions.ToPlanetConfig();
    var hab = HabitabilityCalculator.Compute(planet, orbit, star);
    Console.ForegroundColor = ConsoleColor.DarkCyan;
    Console.WriteLine($"[World] {planet.Name} — Habitability {hab.OverallScore:P0} ({hab.ExpectedLifeComplexity})");
    if (hab.LimitingFactors.Count > 0)
        Console.WriteLine($"[World] Limiting: {string.Join(", ", hab.LimitingFactors)}");
    Console.ResetColor();
}

var generatedBlankPeopleLogFiles = new DirectoryInfo(TFSC.gfiles).GetFiles().ToImmutableList();

var ids = new Dictionary<string, Guid>();
const string so = "significantOther";
const string fr = "friend";
const string frso = "friendSignificantOther";

foreach (var file in generatedBlankPeopleLogFiles)
{
    using (var reader = new StreamReader(file.OpenRead()))
    {
        ids.Add(file.Name.Split("_")[1], Guid.Parse(reader.ReadLine()!));
    }
}

var soid = ids.FirstOrDefault(npc => npc.Key == so).Value;
var friendId = ids.FirstOrDefault(npc => npc.Key == fr).Value;
var friendSOId = ids.FirstOrDefault(npc => npc.Key == frso).Value;

var significantOther = manager.Characters.First(character => character.Person.Id.Value.Equals(soid));

var friend = manager.Characters.First(ch => ch.Person.Id.Value.Equals(friendId));
var friendSO = manager.Characters.First(ch => ch.Person.Id.Value.Equals(friendSOId));

var playerPerson = player.Person;
var significantOtherPerson = significantOther.Person;
var friendPerson = friend.Person;
var friendSOPerson = friendSO.Person;

Console.WriteLine("Before simulation starts:");

var diary = new List<NarrativeEntry>();

var rng = new Random();

var worldMap = WorldMapLoader.Load();
var locationService = new DefaultLocationService();
worldMap.RegisterAllLocations(locationService);
var objectProvider = new CsvWorldObjectProvider();
var speedProvider = new DefaultMovementSpeedProvider();

var mainCharactersLocations = worldMap.GetLocationsInRegion("Castle");

var mainCharactersQuery = from mainCharacters in manager.Characters
                          where mainCharacters.Person.Id.Value == playerPerson.Id.Value || mainCharacters.Person.Id.Value == soid || mainCharacters.Person.Id.Value == friendId || mainCharacters.Person.Id.Value ==friendSOId
                          select mainCharacters;

var mainCharactersPersonQuery = from mainCharacters in mainCharactersQuery
                                select mainCharacters.Person;

var locationQuery = from locations in mainCharactersPersonQuery
                    where locations.Snapshot.InteractionSurface.Location == "Unknown"
                    select locations;

foreach (var personToMove in locationQuery)
{
    locationService.MoveCharacter(personToMove.Id, mainCharactersLocations[rng.Next(0, mainCharactersLocations.Count)]);
}

Console.WriteLine($"{nameof(mainCharactersPersonQuery)}: {mainCharactersPersonQuery.Count()}, {nameof(mainCharactersQuery)}: {mainCharactersQuery.Count()}, {nameof(locationQuery)}, {locationQuery.Count()}");

foreach (var mainCharacter in mainCharactersPersonQuery.ToList())
{
    string slotId = $"sl.{mainCharacter.Id.Value.ToString()}";
    var slot = new ScheduleSlot(slotId, 13, ActionNames.SelfCare, "stables");
    mainCharacter.ReceiveEvent(new ScheduleSlotTriggered(startNow.AddDays(1), mainCharacter.Id, slotId, ActionNames.SelfCare, "stables", 0.65));

    Console.WriteLine("Slot: {0}", slotId);
}

Console.WriteLine("Press any key to continue...");
Console.ReadKey();

var mainCharactersSceneOpts = new SimulationSceneOptions
{
    Characters = [playerPerson, significantOtherPerson, friendPerson, friendSOPerson],
    LocationService = locationService,
    AstroConfig = astroOptions,
    UniverseConfig = universeOptions,
    SimulationDays = simulationDays,
    TickStep = WTimeSpan.FromHours(0.5),
    InternalSubstep = WTimeSpan.FromMinutes(5),
    NarrativeFormatter = new DefaultNarrativeFormatter(),
    DefaultCharacterLod = CognitiveResolutionLevel.Nearby,
    ResolveCharacterLod = character => SceneCharacterLodResolver.Resolve(character, playerPerson.Id, locationService, new HashSet<HumanId>
    {
        playerPerson.Id,
        significantOtherPerson.Id
    }),
    ResolveCharacter = id =>
    {
        var chars = new[] { playerPerson, significantOtherPerson, friendPerson, friendSOPerson };
        var found = chars.FirstOrDefault(c => c.Id == id);

        return found is not null
            ? new NarrativeCharacterInfo(found.Identity.FirstName.Original, found.Biology)
            : new NarrativeCharacterInfo(id.Value.ToString()[..8], GameEngineTools.Characters.Core.SexBiology.Unknown);
    },

    OnNarrative = entry =>
    {
        diary.Add(entry);

        //if (entry.Priority == NarrativePriority.High)
        //{
        //    Console.ForegroundColor = ConsoleColor.Yellow;
        //    Console.WriteLine($"* [{entry.OccurredAt}] {entry.Text}");
        //    Console.ResetColor();
        //}
        //else if (entry.Priority == NarrativePriority.Medium)
        //{
        //    Console.WriteLine($"  [{entry.OccurredAt}] {entry.Text}");
        //}
    },

    OnTick = (now, chars) =>
    {
        // ── First impressions — all unmet pairs sharing a location ────────────
        // Replaces the old hardcoded p/so pair check.
        FireFirstImpressions(now, chars, attractionCalculator, locationService, perceptionPolicy, perceptionOptions);

        // ── NPC movement — route MoveTo:* actions from previous tick ─────────
        RouteMoveTo(now, chars, locationService, worldMap, speedProvider, rng);

        DynamicReachOutRouting(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);

        OrganicMicroPositives(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);

        HandleChildBornEvents(now, chars, familyGraph, manager, gf, locationService, runtime.Services);

        Console.Title = now.Date.ToString();
    }
};

var mainCharactersScene = new SimulationScene(clock, mainCharactersSceneOpts, lodRuntime);
await mainCharactersScene.RunAsync();

var characters = new List<IHuman>();

foreach (var character in manager.Characters.Select(c => c.Person).ToList())
{
    var mainCharacters = mainCharactersPersonQuery.ToList();
    if (mainCharacters.Contains(character))
    {
        continue;
    }

    //if (character.Identity.BirthDate < startNow.Date)
    //    continue;

    characters.Add(character);
}

if (characters.Count > 0)
{
    var ocLocations = worldMap.GetLocationsInRegion("Village");

    foreach (var character in characters)
    {
        locationService.MoveCharacter(character.Id, ocLocations[rng.Next(0, ocLocations.Count)]);
    }

    clock.SetNow(startNow);

    var fullyMetLocations = new HashSet<string>(StringComparer.Ordinal);

    var socialChars = new List<IHuman>(characters.Count);

    var otherCharactersScene = new SimulationScene(clock, new SimulationSceneOptions
    {
        Characters = characters,
        LocationService = locationService,
        TickStep = WTimeSpan.FromHours(5),
        AstroConfig = astroOptions,
        UniverseConfig = universeOptions,
        SimulationDays = simulationDays,
        DefaultCharacterLod = CognitiveResolutionLevel.Background,
        InternalSubstep = WTimeSpan.FromMinutes(30),
        OnTick = (now, chars) =>
        {
            socialChars.Clear();
            var allChars = chars;

            foreach (var ch in chars)
            {
                var lod = lodRuntime.Get(ch.Id);

                if (lod != CognitiveResolutionLevel.Background)
                    socialChars.Add(ch);
            }

            FireFirstImpressions(now, chars, attractionCalculator, locationService, perceptionPolicy, perceptionOptions);

            RouteMoveTo(now, chars, locationService, worldMap, speedProvider, rng);

            if (socialChars.Count > 0)
            {
                DynamicReachOutRouting(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);

                OrganicMicroPositives(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);
            }

            HandleChildBornEvents(now, chars, familyGraph, manager, gf, locationService, runtime.Services, fullyMetLocations);

            Console.Title = now.Date.ToString();
        }
    }, lodRuntime);

    await otherCharactersScene.RunAsync();
}

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

if (canExportMainCharactersInfo)
{
    await File.WriteAllTextAsync(
        Path.Combine(desktopPath, $"player.{playerPerson.Id.Value.ToString()}.txt"),
        player.PrintInfo(false));

    await File.WriteAllTextAsync(
        Path.Combine(desktopPath, $"{so}.{significantOtherPerson.Id.Value.ToString()}.txt"),
        significantOther.PrintInfo(false));

    await File.AppendAllTextAsync(
        Path.Combine(desktopPath, $"{fr}.{friendPerson.Id.Value.ToString()}.txt"),
        friend.PrintInfo(false));

    await File.AppendAllTextAsync(
        Path.Combine(desktopPath, $"{frso}.{friendSOPerson.Id.Value.ToString()}.txt"),
        friendSO.PrintInfo(false));
}

if (canGeneratePrompts)
{
    var promptDir = Directory.CreateDirectory(Path.Combine(desktopPath, "Prompts")).FullName;
    foreach (var character in manager.Characters)
    {
        await File.WriteAllTextAsync(Path.Combine(promptDir, $"{character.Person.Id.Value.ToString()}.txt"), character.PrintPortraitInfo(
            runtime.Services.GetRequiredService<IPortraitSpecBuilder>(), runtime.Services.GetRequiredService<IPortraitPromptFormatter>()));
    }
}

Console.WriteLine("Simulation complete. Game time: {0}", clock.Now);
Console.ForegroundColor = ConsoleColor.DarkGray;
Console.WriteLine("Simulation start time was: {0}. Simulation end time: {1}. Simulation days: {2}", startNow.ToString(), clock.Now.ToString(), simulationDays.ToString());
Console.WriteLine("Simulation starts at {0} and should end at {1}", startNow.ToString(), startNow.AddDays(simulationDays).ToString());
Console.ResetColor();

if (canExportDiary)
{
    File.WriteAllText(
        Path.Combine(desktopPath, $"diary.{startNow.Date.ToString()} - {clock.Now.Date.ToString()}.txt"),
        sbDiary.ToString());
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();

# region Helper Methods

static void DynamicReachOutRouting(WDateTime now, IReadOnlyList<IHuman> chars, ILocationService locationService, Random rng, IPerceptionFidelityPolicy perceptionPolicy, CharacterPerceptionOptions perceptionOptions)
{
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

        var candidates = CharacterPerceptionResolver.GetPerceivedCharacters(character, chars, locationService, perceptionPolicy, perceptionOptions);

        if (candidates.Count == 0)
            continue;

        var targetMode = character.Snapshot.Behavior.NeedIntimacy >= 55
            ? SocialTargetMode.Intimacy
            : SocialTargetMode.ReachOut;
        var target = SemanticTargeting.ChooseTarget(character, candidates, targetMode);
        if (target is null)
            continue;

        var selection = ReachOutSpeechActSelector.SelectSpeechAct(character, target, now, rng);
        var act = selection.Act;

        Console.WriteLine(
            "[ReachOut] {0} -> {1}: action={2}, familiarity={3:F1}, trust={4:F1}, comfort={5:F1}, closeness={6:F1}, romantic={7:F1}, privacy={8}, character's gender={9}, target's gender={10}.",
            character.Id.Value,
            target.Id.Value,
            act,
            selection.Familiarity,
            selection.Trust,
            selection.Comfort,
            selection.Closeness,
            selection.RomanticInterest,
            selection.HasPrivacy ? "yes" : "no",
            character.Biology.ToString(),
            target.Biology.ToString());

        target.ReceiveEvent(new InteractionProposed(now, character.Id, target.Id, act, null, character.Biology));
        TryTouch(now, character, target, rng);
    }
}

static void OrganicMicroPositives(WDateTime now, IReadOnlyList<IHuman> chars, ILocationService locationService, Random rng, IPerceptionFidelityPolicy perceptionPolicy, CharacterPerceptionOptions perceptionOptions)
{
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

        var witnesses = CharacterPerceptionResolver.GetPerceivedCharacters(character, chars, locationService, perceptionPolicy, perceptionOptions);

        if (witnesses.Count == 0)
            continue;

        // Pick one random witness — only one MicroPositive per creative action.
        var witness = witnesses[rng.Next(witnesses.Count)];

        if (rng.NextDouble() < 0.30)
            character.ReceiveEvent(new MicroPositive(now, witness.Id, character.Id, MemoryMicroEventKinds.Validation));
    }
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
    var hasPrivacy = from.Snapshot.InteractionSurface.HasPrivacy;
    var level = ReachOutTouchSelector.SelectTouchLevel(edge, hasPrivacy, rng);
    if (level is not null)
    {
        to.ReceiveEvent(new TouchAttempted(now, from.Id, to.Id, level.Value));
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
/// Fires <see cref="FirstImpressionFormed"/> for every perceivable pair of characters
/// that share a location and have not yet met (no relationship edge exists).
/// Each ordered pair (A→B and B→A) is processed exactly once per call.
/// </summary>
/// <param name="now">Current simulation time.</param>
/// <param name="chars">All characters in the scene.</param>
/// <param name="calculator">Shared attraction calculator singleton.</param>
/// <param name="locations">Location service used to group characters by place.</param>
/// <param name="perceptionPolicy">Runtime perception fidelity policy (LOD-backed).</param>
/// <param name="perceptionOptions">Noise and crowding thresholds for perception tiers.</param>
/// <param name="fullyMetLocations">
/// Optional caller-owned cache of location ids where every perceivable pair has already met.
/// When provided:
/// <list type="bullet">
///   <item>Locations present in the set are skipped entirely — no perceivedBy scan.</item>
///   <item>A location is added to the set after a pass produces zero new impressions.</item>
/// </list>
/// Invalidation responsibility lies with <see cref="RouteMoveTo"/> — it removes a location
/// from the set whenever a character moves into it.
/// Pass <c>null</c> to disable caching (correct for small scenes like mainScene).
/// </param>
static void FireFirstImpressions(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    IAttractionCalculator calculator,
    ILocationService locations,
    IPerceptionFidelityPolicy perceptionPolicy,
    CharacterPerceptionOptions perceptionOptions,
    HashSet<string>? fullyMetLocations = null)
{
    // Build a scene-wide id → IHuman lookup for O(1) resolution inside the loops.
    var byId = chars.ToDictionary(c => c.Id);

    // Collect occupied location ids — one O(N) pass, no duplicates.
    var occupiedLocations = chars
        .Select(c => locations.GetLocation(c.Id))
        .Where(loc => loc is not null)
        .Distinct()!;

    foreach (var locationId in occupiedLocations)
    {
        // ── Fast path: saturated location ────────────────────────────────────
        // Every perceivable pair in this location has already met.
        // Skip the perceivedBy build entirely — the most expensive part.
        if (fullyMetLocations is not null && fullyMetLocations.Contains(locationId))
            continue;

        // ── Resolve local characters ──────────────────────────────────────────
        // Work only with chars registered at this location — O(k) not O(N).
        // Passing localChars to GetPerceivedCharacters means the internal
        // same-location filter iterates k items instead of N.
        var localChars = locations
            .GetCharactersAt(locationId)
            .Select(id => byId.TryGetValue(id, out var h) ? h : null)
            .OfType<IHuman>()
            .ToList();

        if (localChars.Count < 2)
        {
            // Single occupant — nothing to pair. Mark saturated immediately.
            fullyMetLocations?.Add(locationId);
            continue;
        }

        // ── Per-location perceivedBy — O(k²) not O(N²) ───────────────────────
        // Previously built upfront for ALL N chars every substep. Now built only
        // for the k chars in this location, and only when not cached.
        // Passing localChars as the candidate list short-circuits the O(N) scan
        // inside GetPerceivedCharacters — it already knows they are co-located.
        var perceivedBy = localChars.ToDictionary(
            c => c.Id,
            c => CharacterPerceptionResolver
                .GetPerceivedCharacters(c, localChars, locations, perceptionPolicy, perceptionOptions)
                .Select(x => x.Id)
                .ToHashSet());

        var anyNewImpression = false;

        // ── Unique-pair loop: A→B and B→A in a single pass ───────────────────
        for (var i = 0; i < localChars.Count; i++)
        {
            for (var j = i + 1; j < localChars.Count; j++)
            {
                var a = localChars[i];
                var b = localChars[j];

                // Mutual perception required — both must notice each other.
                if (!perceivedBy[a.Id].Contains(b.Id)) continue;
                if (!perceivedBy[b.Id].Contains(a.Id)) continue;

                // Skip pairs that already share a relationship edge — they have met.
                if (a.Snapshot.Relationships.Edges.ContainsKey(b.Id)) continue;

                anyNewImpression = true;

                var viewA = AppearanceProjector.Compute(
                    a.PhysicalAppearance, a.Snapshot.Physiology, a.Biology, a.Snapshot.Physiology.Aging);
                var viewB = AppearanceProjector.Compute(
                    b.PhysicalAppearance, b.Snapshot.Physiology, b.Biology, b.Snapshot.Physiology.Aging);

                // A sees B.
                var aResult = a.AttractionProfile is not null
                    ? calculator.Calculate(
                        a.AttractionProfile, b.PhysicalAppearance, viewB, b.Biology,
                        observerValence: a.Snapshot.Psychology.Valence,
                        observerArousal: a.Snapshot.Psychology.Arousal,
                        observerAgeYears: a.Age,
                        targetAgeYears: b.Age)
                    : AttractionResult.Neutral;

                // B sees A.
                var bResult = b.AttractionProfile is not null
                    ? calculator.Calculate(
                        b.AttractionProfile, a.PhysicalAppearance, viewA, a.Biology,
                        observerValence: b.Snapshot.Psychology.Valence,
                        observerArousal: b.Snapshot.Psychology.Arousal,
                        observerAgeYears: b.Age,
                        targetAgeYears: a.Age)
                    : AttractionResult.Neutral;

                a.ReceiveEvent(new FirstImpressionFormed(now, a.Id, b.Id,
                    aResult.FirstImpressionLike, aResult.Score,
                    aResult.BasePhysical, aResult.PreferenceMatch));
                b.ReceiveEvent(new FirstImpressionFormed(now, b.Id, a.Id,
                    bResult.FirstImpressionLike, bResult.Score,
                    bResult.BasePhysical, bResult.PreferenceMatch));
            }
        }

        // ── Cache saturation ──────────────────────────────────────────────────
        // No new impression fired → every perceivable pair in this location has met.
        // Mark as saturated; future substeps skip the perceivedBy scan.
        // RouteMoveTo removes this location from the set when a newcomer arrives.
        if (!anyNewImpression)
            fullyMetLocations?.Add(locationId);
    }
}

/// <summary>
/// Routes <c>MoveTo:*</c> actions emitted by <see cref="DefaultBehaviorEngine"/>
/// to a concrete location via <see cref="ILocationService"/>.
/// </summary>
/// <remarks>
/// <para>
/// The engine emits the intent (e.g. <c>"MoveTo:Social"</c>). This method resolves
/// a concrete destination using the world adjacency graph — adjacent locations of the
/// requested type are preferred, ordered by travel duration at the character's current
/// movement speed. Falls back to any registered location of the type when no adjacent
/// match exists.
/// </para>
/// <para>
/// When a character is moved, the destination is removed from
/// <paramref name="fullyMetLocations"/> so <c>FireFirstImpressions</c> rescans it
/// on the next substep — the arriving character may be a stranger to everyone there.
/// </para>
/// </remarks>
/// <param name="now">Current simulation time.</param>
/// <param name="chars">All characters whose outboxes are inspected for <c>MoveTo:*</c> actions.</param>
/// <param name="locations">Location service that performs the actual move.</param>
/// <param name="worldMap">World adjacency graph used to prefer nearby destinations.</param>
/// <param name="speedProvider">Provides movement speed in metres per minute per character.</param>
/// <param name="rng">Random source — used only in the scored fallback path.</param>
/// <param name="fullyMetLocations">
/// Optional first-impression saturation cache. The destination location is removed
/// from this set whenever a character moves into it.
/// Pass <c>null</c> to disable cache invalidation (correct for mainScene with 4 chars).
/// </param>
static void RouteMoveTo(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    ILocationService locations,
    WorldMap worldMap,
    IMovementSpeedProvider speedProvider,
    Random rng,
    HashSet<string>? fullyMetLocations = null)
{
    foreach (var character in chars)
    {
        var moveTo = character.LastOutbox
            .OfType<ActionCommitted>()
            .FirstOrDefault(a => a.ActionName.StartsWith("MoveTo:"));

        if (moveTo is null)
            continue;

        // Parse the requested LocationType from the action name suffix.
        var typeName = moveTo.ActionName["MoveTo:".Length..];
        if (!Enum.TryParse<LocationType>(typeName, out var requestedType))
            continue;

        var currentLocation = locations.GetLocation(character.Id);

        var chosen = FindBestMoveTarget(
            character, locations, worldMap, speedProvider, currentLocation, requestedType);

        if (chosen is null)
        {
            Console.WriteLine(
                $"[MoveTo] {character.Id.Value} requested {requestedType}, " +
                $"but no suitable alternative location exists.");
            continue;
        }

        locations.MoveCharacter(character.Id, chosen);

        // ── Cache invalidation ────────────────────────────────────────────────
        // A character entering a location may be a stranger to everyone there.
        // Remove it from the saturation cache so FireFirstImpressions rescans
        // that location on the next substep.
        fullyMetLocations?.Remove(chosen);
    }
}

static string? FindBestMoveTarget(
    IHuman character,
    ILocationService locations,
    WorldMap worldMap,
    IMovementSpeedProvider speedProvider,
    string? currentLocationId,
    LocationType requestedType)
{
    if (currentLocationId is null)
        return null;

    var speed = speedProvider.GetSpeedMetersPerMinute(character.Snapshot);

    // Prefer adjacent locations of the requested type (adjacency-graph-based selection)
    var adjacentTarget = worldMap
        .GetConnections(currentLocationId)
        .Select(conn => (conn, descriptor: worldMap.GetLocation(conn.TargetLocationId)))
        .Where(t => t.descriptor?.Type == requestedType)
        .OrderBy(t => TravelDurationComputer.ComputeMinutes(t.conn.DistanceMeters, speed))
        .Select(t => t.conn.TargetLocationId)
        .FirstOrDefault();

    if (adjacentTarget is not null)
        return adjacentTarget;

    // Fallback: any location of the requested type that is not the current one,
    // scored by noise/crowding/privacy heuristic.
    var candidates = locations
        .GetLocationsByType(requestedType)
        .Where(id => id != currentLocationId)
        .Select(id => new
        {
            Id = id,
            Descriptor = locations.GetDescriptor(id),
            Occupants = locations.GetCharactersAt(id).Count
        })
        .Where(x => x.Descriptor is not null)
        .Select(x => new
        {
            x.Id,
            Desc = x.Descriptor!,
            x.Occupants,
            Noise = Math.Clamp(
                x.Descriptor!.BaseNoise + x.Descriptor.NoisePerPerson * x.Occupants,
                0.0,
                1.0),
            Crowding = Math.Clamp(
                x.Descriptor!.Capacity > 0 ? (double)x.Occupants / x.Descriptor.Capacity : 1.0,
                0.0,
                1.0),
            Privacy = x.Descriptor!.AllowsPrivacy && x.Occupants <= 1
        })
        .ToList();

    if (candidates.Count == 0)
        return null;

    var scored = candidates
        .Select(x => new
        {
            x.Id,
            Score = ScoreMoveTarget(character, requestedType, x.Noise, x.Crowding, x.Privacy)
        })
        .OrderByDescending(x => x.Score)
        .ToList();

    var best = scored[0];

    // ochrana proti "move for no real gain" můžeš později zpřísnit
    return best.Score > 0.0 ? best.Id : null;
}

/// <summary>
/// Handles <see cref="ChildBorn"/> events emitted by the mother's PhysiologyEngine.
/// Creates the newborn, wires all family bonds, places the child at the mother's
/// location, and invalidates the first-impression saturation cache for that location.
/// </summary>
/// <remarks>
/// <para>
/// <b>Architectural note — newborn is NOT ticked in the current scene.</b><br/>
/// <see cref="SimulationSceneOptions.Characters"/> is a closed <c>IReadOnlyList</c>
/// captured at scene start. Adding the newborn to <paramref name="manager"/> makes
/// them available in the next simulation run; they will not tick during this session.
/// This is intentional — mid-session scene expansion is not supported.
/// </para>
/// <para>
/// <b>Location placement:</b> The newborn is placed at the mother's current location
/// so they appear in <see cref="ILocationService.GetCharactersAt"/> immediately.
/// The corresponding entry in <paramref name="fullyMetLocations"/> is removed so
/// <c>FireFirstImpressions</c> rescans that location on the next substep.
/// </para>
/// </remarks>
/// <param name="now">Current simulation time.</param>
/// <param name="chars">Characters whose outboxes are scanned for <see cref="ChildBorn"/>.</param>
/// <param name="familyGraph">Scene-level family registry updated with new bonds.</param>
/// <param name="manager">Global character registry — newborn is appended here.</param>
/// <param name="gf">Generated-file exporter — persists the newborn's JSON.</param>
/// <param name="locations">Location service used to place the newborn at the mother's location.</param>
/// <param name="services">DI service provider for blueprint and factory resolution.</param>
/// <param name="fullyMetLocations">
/// Optional first-impression saturation cache. When the newborn is placed, the
/// mother's location is removed from this set — the newborn is a stranger to everyone
/// already there.
/// </param>
static void HandleChildBornEvents(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    FamilyGraph familyGraph,
    GameEngineToolsManager manager,
    GeneratedFile gf,
    ILocationService locations,
    IServiceProvider services,
    HashSet<string>? fullyMetLocations = null)
{
    foreach (var character in chars)
    {
        // ChildBorn is emitted by the MOTHER's PhysiologyEngine.
        // ParentA = mother (character.Id), ParentB = father (pregnancy.OtherParent).
        var childBornEvent = character.LastOutbox
            .OfType<ChildBorn>()
            .FirstOrDefault();

        if (childBornEvent is null)
            continue;

        var mother = character;
        var father = chars.FirstOrDefault(c => c.Id == childBornEvent.ParentB);

        if (father is null)
        {
            Console.WriteLine(
                $"[ChildBorn] Warning: father {childBornEvent.ParentB.Value} " +
                $"not found in scene — skipping newborn creation.");
            continue;
        }

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            $"[ChildBorn] {mother.Identity.FirstName} + {father.Identity.FirstName} → new child!");
        Console.ResetColor();

        // ── Step 1: Generate the newborn blueprint from both parents ──────────
        var childBlueprintGen = services.GetRequiredService<IChildBlueprintGenerator>();
        var humanFactory = services.GetRequiredService<IHumanFactory>();

        var childBlueprint = childBlueprintGen.Generate(
            parentA: father,
            parentB: mother,
            bornOn: now.Date,
            seed: null);

        // ── Step 2: Create the IHuman ─────────────────────────────────────────
        // IHumanFactory.Create() automatically calls SeedFromPersonality and
        // SeedFromOccupation(OccupationKind.None) — no manual seeding needed.
        var newborn = humanFactory.Create(childBlueprint);

        // ── Step 3: Wire family bonds into FamilyGraph ────────────────────────
        FamilyBuilder.WireNewborn(familyGraph, father, mother, newborn, now);

        // Sibling bonds — GetKin(father, KinRole.Parent) returns characters that
        // father is a Parent of, i.e. his existing children = newborn's siblings.
        var existingSiblings = familyGraph
            .GetKin(father.Id, KinRole.Parent)
            .Select(k => k.RelativeId)
            .Where(id => id != newborn.Id)
            .Select(id => chars.FirstOrDefault(c => c.Id == id))
            .OfType<IHuman>()
            .ToList();

        foreach (var sibling in existingSiblings)
            FamilyBuilder.AddSiblingBond(familyGraph, newborn, sibling, now);

        // Paternal grandparents — GetKin(father, KinRole.Child) returns characters
        // that father is a Child of, i.e. father's own parents.
        foreach (var link in familyGraph.GetKin(father.Id, KinRole.Child))
        {
            var grandparent = chars.FirstOrDefault(c => c.Id == link.RelativeId);
            if (grandparent is not null)
                FamilyBuilder.AddGrandparentBond(familyGraph, grandparent, newborn, now);
        }

        // Maternal grandparents.
        foreach (var link in familyGraph.GetKin(mother.Id, KinRole.Child))
        {
            var grandparent = chars.FirstOrDefault(c => c.Id == link.RelativeId);
            if (grandparent is not null)
                FamilyBuilder.AddGrandparentBond(familyGraph, grandparent, newborn, now);
        }

        // ── Step 4: Flush inbox so FamilyBondSeeded edges are applied ─────────
        newborn.FlushInbox();

        // ── Step 5: Place newborn at the mother's current location ────────────
        // Without placement the newborn has no location and GetCharactersAt()
        // never returns them — FireFirstImpressions would not see them at all.
        var motherLocation = locations.GetLocation(mother.Id);
        if (motherLocation is not null)
        {
            locations.MoveCharacter(newborn.Id, motherLocation);

            // Invalidate the saturation cache for this location — the newborn
            // is a stranger to everyone already there. FireFirstImpressions will
            // rescan next substep and fire FirstImpressionFormed for each pair.
            fullyMetLocations?.Remove(motherLocation);
        }

        // ── Step 6: Register in global manager and persist ────────────────────
        // NOTE: The newborn is NOT added to the current scene's character list.
        // SimulationSceneOptions.Characters is a closed IReadOnlyList captured at
        // scene start — mid-session expansion is not supported. The newborn will
        // be ticked starting from the NEXT simulation run (imported via GeneratedFile).
        var npc = new NPC(maxHealth: 100, person: newborn);
        manager.Characters.Add(npc);
        gf.Export(npc);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(
            $"[ChildBorn] Newborn placed at '{motherLocation ?? "unknown"}', " +
            $"exported: {newborn.Identity.FirstName} ({newborn.Biology}, age=0) " +
            $"id={newborn.Id.Value}");
        Console.ResetColor();
    }
}

static double ScoreMoveTarget(
    IHuman character,
    LocationType requestedType,
    double noise,
    double crowding,
    bool privacy)
{
    var stress = character.Snapshot.Psychology.Stress / 100.0;

    return requestedType switch
    {
        LocationType.Work =>
            (1.0 - noise) * 0.45 +
            (1.0 - crowding) * 0.40 +
            (privacy ? 0.15 : 0.0),

        LocationType.Rest =>
            (1.0 - noise) * 0.50 +
            (1.0 - crowding) * 0.20 +
            (privacy ? 0.30 : 0.0) +
            stress * 0.15,

        LocationType.Private =>
            (1.0 - noise) * 0.35 +
            (1.0 - crowding) * 0.20 +
            (privacy ? 0.45 : 0.0) +
            stress * 0.20,

        LocationType.Social =>
            crowding * 0.45 +
            (1.0 - noise) * 0.15 +
            (privacy ? -0.10 : 0.0),

        LocationType.Public =>
            (1.0 - crowding) * 0.35 +
            (1.0 - noise) * 0.15,

        _ => 0.0
    };
}

#endregion
