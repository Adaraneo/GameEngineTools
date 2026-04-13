// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Interactions;
using GameEngineTools.Characters.Engines.Memory;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.SemanticMemory;
using GameEngineTools.Characters.Generation.Portraits;
using GameEngineTools.Characters.Hosting;
using GameEngineTools.Characters.Traits;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.Narrative;
using GameEngineTools.World.Location;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
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
var lodRuntime = runtime.Services.GetRequiredService<ICognitiveResolutionLevelRuntime>();
var perceptionPolicy = runtime.Services.GetRequiredService<IPerceptionFidelityPolicy>();

// ── Characters ────────────────────────────────────────────────────────────────
var player = gf.ImportPC(new FileInfo(Directory.GetFiles(gf.PlayerDirectory).First()).Name);
manager.Characters.Add(player);

clock.SetNow(initTicks == defaultTicks
    ? WDateTime.New(player.Person.Identity.BirthDate.AddYears(16))
    : new WDateTime(initTicks));

foreach (var filename in Directory.GetFiles(gf.NPCDirectory))
    manager.Characters.Add(gf.ImportNPC(new FileInfo(filename).Name));

#region input settings

Console.Write("Would you like to export prompts after simulations is complete? [y\\N] \b");
bool canGeneratePrompts = false;
var answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canGeneratePrompts = true;

Console.Clear();
Console.Write("Would you like to export players and significants other's info after simulation? [y\\N] \b");
bool canExportPlayersAndSOInfos = false;
answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canExportPlayersAndSOInfos = true;

Console.Clear();
Console.Write("Would you like to export diary after simulation? [y\\N] \b");
bool canExportDiary = false;
answerKey = Console.ReadKey().Key;
if (answerKey == ConsoleKey.Y)
    canExportDiary = true;

int simulationYears = 2;

SetYearsForSimulation(simulationYears);

static void SetYearsForSimulation(int simulationYears, bool printInfo = true)
{
    Console.Clear();
    Console.Write("Set the year(s) for simulation: ");
    var answer = Console.ReadLine();
    if (answer.Length == 0 && !int.TryParse(answer, out simulationYears))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("The simulation years are not set or are in incorrect format.");
        Console.ResetColor();
        Console.WriteLine("Would you like to try it again? [y\\N]");
        if (printInfo)
        {
            Console.WriteLine("If you answer no (n), simulation years will be set to 2.");
        }

        var answerKey = Console.ReadKey().Key;
        if (answerKey == ConsoleKey.Y)
        {
            SetYearsForSimulation(simulationYears, false);
        }
    }
}

#endregion

var currDir = Directory.GetCurrentDirectory();

var configProvider = new ConfigurationBuilder().SetBasePath(currDir).AddJsonFile("appsettings.World.json").Build();
var perceptionOptions = configProvider.GetSection("World:Perception").Get<CharacterPerceptionOptions>() ?? new CharacterPerceptionOptions();

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

var diary = new List<NarrativeEntry>();

var rng = new Random();

var locationService = new DefaultLocationService();

locationService.RegisterLocation(new LocationDescriptor(
    Id: "village_square",
    DisplayName: "Village Square",
    BaseNoise: 0.3,
    NoisePerPerson: 0.1,
    Capacity: 20,
    AllowsPrivacy: false,
    LocationType.Social));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_hall",
    DisplayName: "Castle Hall",
    BaseNoise: 0.1,
    NoisePerPerson: 0.05,
    Capacity: 10,
    AllowsPrivacy: true,
    LocationType.Private));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_sleep_room",
    DisplayName: "Castle Sleep Room",
    BaseNoise: 0.1,
    NoisePerPerson: 0.01,
    Capacity: 10,
    AllowsPrivacy: true,
    LocationType.Rest));

for (int villageHouseIndex = 0; villageHouseIndex < (manager.Characters.Count - 3) / 5; villageHouseIndex++)
{
    locationService.RegisterLocation(new LocationDescriptor(
        $"village_house{villageHouseIndex}",
        $"Village House {villageHouseIndex + 1}",
        BaseNoise: 0.2,
        NoisePerPerson: 0.2,
        Capacity: 5,
        false,
        LocationType.Private));
    locationService.RegisterLocation(new LocationDescriptor(
        Id: $"village_house{villageHouseIndex}_sleep_room",
        DisplayName: $"Village House {villageHouseIndex + 1}: Sleep Room",
        BaseNoise: 0.2,
        NoisePerPerson: 0.2,
        Capacity: 2,
        AllowsPrivacy: true,
        LocationType.Rest));
}

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_horse_stables",
    DisplayName: "Castle Horse Stables",
    BaseNoise: 0.3,
    NoisePerPerson: 0.2,
    Capacity: 10,
    AllowsPrivacy: false,
    LocationType.Work));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "castle_forge",
    DisplayName: "Castle Forge",
    BaseNoise: 0.7,
    NoisePerPerson: 0.2,
    Capacity: 10,
    AllowsPrivacy: false,
    LocationType.Work));

locationService.RegisterLocation(new LocationDescriptor(
    Id: "forest_behinf_village",
    DisplayName: "Forest Behing the vilage",
    BaseNoise: 0.3,
    NoisePerPerson: 0.1,
    Capacity: 100,
    AllowsPrivacy: true,
    LocationType.Public));

if (locationService.GetLocation(playerPerson.Id) is null && locationService.GetLocation(significantOtherPerson.Id) is null && locationService.GetLocation(friendPerson.Id) is null && locationService.GetLocation(friendSOPerson.Id) is null)
{
    locationService.MoveCharacter(playerPerson.Id, "village_square");
    locationService.MoveCharacter(significantOtherPerson.Id, "village_square");
    locationService.MoveCharacter(friendPerson.Id, "village_square");
    locationService.MoveCharacter(friendSOPerson.Id, "village_square");
}

foreach (var npc in manager.Characters.Where(npc => ids.ContainsKey(npc.Person.Id.Value.ToString()) == false))
{
    if (locationService.GetLocation(npc.Person.Id) is null)
    {
        locationService.MoveCharacter(npc.Person.Id, "village_square");
    }
}

var mainTrioSceneOpts = new SimulationSceneOptions
{
    Characters = [playerPerson, significantOtherPerson, friendPerson, friendSOPerson],
    LocationService = locationService,
    SimulationYears = simulationYears,
    TickStep = WTimeSpan.FromHours(0.5),
    InternalSubstep = WTimeSpan.FromMinutes(5),
    NarrativeFormatter = new DefaultNarrativeFormatter(),
    DefaultCharacterLod = CognitiveResolutionLevel.Background,
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
        RouteMoveTo(now, chars, locationService, rng);

        // ── Location context — move both to Castle on day 16, evening ─────────
        if (now.Day is 16 && now.Hour is 20
        && !locationService.GetLocation(significantOtherPerson.Id)!.Equals("castle_hall", StringComparison.InvariantCultureIgnoreCase)
        && !locationService.GetLocation(playerPerson.Id)!.Equals("castle_hall", StringComparison.InvariantCultureIgnoreCase)
        && !locationService.GetLocation(friendPerson.Id)!.Equals("castle_hall", StringComparison.InvariantCultureIgnoreCase)
        && !locationService.GetLocation(friendSOPerson.Id)!.Equals("castle_hall", StringComparison.InvariantCultureIgnoreCase))
        {
            locationService.MoveCharacter(playerPerson.Id, "castle_hall");
            locationService.MoveCharacter(significantOtherPerson.Id, "castle_hall");
            locationService.MoveCharacter(friendPerson.Id, "castle_hall");
            locationService.MoveCharacter(friendSOPerson.Id, "castle_hall");
        }

        DynamicReachOutRouting(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);

        OrganicMicroPositives(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);
    }
};

var mainTrioScene = new SimulationScene(clock, mainTrioSceneOpts, lodRuntime);
await mainTrioScene.RunAsync();

var characters = manager.Characters.Where(c => c.Person.Id != playerPerson.Id && c.Person.Id != significantOtherPerson.Id && c.Person.Id != friendPerson.Id).Select(c => c.Person).ToList();

if (characters.Count > 0)
{
    clock.SetNow(clock.Now.AddYears(-mainTrioSceneOpts.SimulationYears));
    var otherCharactersScene = new SimulationScene(clock, new SimulationSceneOptions
    {
        Characters = characters,
        LocationService = locationService,
        TickStep = WTimeSpan.FromHours(5),
        SimulationYears = 5,
        DefaultCharacterLod = CognitiveResolutionLevel.Background,
        ResolveCharacterLod = character => SceneCharacterLodResolver.Resolve(character, playerPerson.Id, locationService),
        OnTick = (now, chars) =>
        {
            FireFirstImpressions(now, chars, attractionCalculator, locationService, perceptionPolicy, perceptionOptions);

            RouteMoveTo(now, chars, locationService, rng);

            DynamicReachOutRouting(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);

            OrganicMicroPositives(now, chars, locationService, rng, perceptionPolicy, perceptionOptions);
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

if (canExportPlayersAndSOInfos)
{
    await File.WriteAllTextAsync(
        Path.Combine(desktopPath, $"player.{playerPerson.Id.Value}.txt"),
        player.PrintInfo(false));

    await File.WriteAllTextAsync(
        Path.Combine(desktopPath, $"significantOther.{significantOtherPerson.Id.Value}.txt"),
        significantOther.PrintInfo(false));
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

if (canExportDiary)
{
    File.WriteAllText(
        Path.Combine(desktopPath, $"diary.{clock.Now.Date}.txt"),
        sbDiary.ToString());
}

Console.WriteLine("Press any key to exit...");
Console.ReadKey();

# region Helper Methods

static void DynamicReachOutRouting(WDateTime now,IReadOnlyList<IHuman> chars, ILocationService locationService, Random rng, IPerceptionFidelityPolicy perceptionPolicy, CharacterPerceptionOptions perceptionOptions)
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
            "[ReachOut] {0} -> {1}: action={2}, familiarity={3:F1}, trust={4:F1}, comfort={5:F1}, closeness={6:F1}, romantic={7:F1}, privacy={8}.",
            character.Id.Value,
            target.Id.Value,
            act,
            selection.Familiarity,
            selection.Trust,
            selection.Comfort,
            selection.Closeness,
            selection.RomanticInterest,
            selection.HasPrivacy ? "yes" : "no");

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
    ILocationService locations,
    IPerceptionFidelityPolicy perceptionPolicy,
    CharacterPerceptionOptions perceptionOptions)
{
    // Build a lookup: HumanId → IHuman for O(1) resolve inside the loop.
    var byId = chars.ToDictionary(c => c.Id);
    var perceivedBy = chars.ToDictionary(
    c => c.Id,
    c => CharacterPerceptionResolver.GetPerceivedCharacters(c, chars, locations, perceptionPolicy, perceptionOptions)
    .Select(x => x.Id)
    .ToHashSet());

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

                if (!perceivedBy[a.Id].Contains(b.Id))
                {
                    continue;
                }

                if (!perceivedBy[b.Id].Contains(a.Id))
                {
                    continue;
                }

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

        var chosen = FindBestMoveTarget(character, locations, currentLocation, requestedType);

        if (chosen is null)
        {
            Console.WriteLine($"[MoveTo] {character.Id.Value} requested {requestedType}, but no suitable alternative location exists.");
            continue;
        }

        locations.MoveCharacter(character.Id, chosen);
    }
}

static string? FindBestMoveTarget(
    IHuman character,
    ILocationService locations,
    string? currentLocationId,
    LocationType requestedType)
{
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
