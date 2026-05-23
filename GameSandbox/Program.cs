// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Schedule;
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
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Text;
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
{
    var character = gf.ImportNPC(new FileInfo(filename).Name);
    if (character.Person.Identity.BirthDate > startNow.Date)
        continue;

    manager.Characters.Add(character);
}

// Register all imported characters in FamilyGraph so that kin queries work
// from the first tick. Register() now automatically reconstructs kin links
// from the persisted RelationshipEdge.KinRole, so a single call per character
// is sufficient — no additional setup needed here.
var familyGraph = runtime.Services.GetRequiredService<FamilyGraph>();
foreach (var character in manager.Characters)
{
    familyGraph.Register(character.Person);
}

#region input settings

var currDir = Directory.GetCurrentDirectory();
var configProvider = new ConfigurationBuilder().SetBasePath(currDir).AddJsonFile("appsettings.json").AddJsonFile("appsettings.World.json").Build();

var gsSection = configProvider.GetSection("GameSandbox");
bool canGeneratePrompts = gsSection.GetValue<bool>("CanGeneratePrompts");
bool canExportMainCharactersInfo = gsSection.GetValue<bool>("CanExportMainCharactersInfo");
bool canExportDiary = gsSection.GetValue<bool>("CanExportDiary");

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

var surnamesQuery = from npcs in manager.Characters
                    select npcs.Person.Identity.LastName;

var surnames = surnamesQuery.DistinctBy(ch => ch.Male).Select(c => c.Male).GetEnumerator();

while (surnames.MoveNext())
{
    Console.WriteLine("Family: {0}", surnames.Current);
    foreach (var fam in familyGraph.GetClanMembers(surnames.Current))
    {
        var person = manager.Characters.First(ch => ch.Person.Id == fam)?.Person!;
        Console.WriteLine("Family Member: {0}, Kin roles: {1}, (Age: {2})", person.ToString(), familyGraph.GetKin(fam).Count, person.Age);
    }

    foreach (var fam in familyGraph.GetByName(surnames.Current))
    {
        var person = manager.Characters.First(ch => ch.Person.Id == fam)?.Person!;
        Console.WriteLine("Family Graph Member: {0} (Age: {1})", person.ToString(), person.Age);
    }
}

var diary = new List<NarrativeEntry>();

var rng = new Random();

var worldMap = WorldMapLoader.Load();
var locationService = new DefaultLocationService();
worldMap.RegisterAllLocations(locationService);
var objectProvider = new CsvWorldObjectProvider();
var speedProvider = new DefaultMovementSpeedProvider();

var mainCharactersLocations = worldMap.GetLocationsInRegion("Castle");

var mainCharactersQuery = from mainCharacters in manager.Characters
                          where mainCharacters.Person.Id.Value == playerPerson.Id.Value || mainCharacters.Person.Id.Value == soid || mainCharacters.Person.Id.Value == friendId || mainCharacters.Person.Id.Value == friendSOId
                          select mainCharacters;

var mainCharactersPersonQuery = from mainCharacters in mainCharactersQuery
                                select mainCharacters.Person;

var locationQuery = from locations in mainCharactersPersonQuery
                    where locations.Snapshot.InteractionSurface.Location == "Unknown"
                    select locations;

foreach (var personToMove in locationQuery)
{
    var homeLocationId = mainCharactersLocations[rng.Next(0, mainCharactersLocations.Count)];
    locationService.MoveCharacter(personToMove.Id, homeLocationId);
    personToMove.SetHomeLocation(homeLocationId);
}

Console.WriteLine($"{nameof(mainCharactersPersonQuery)}: {mainCharactersPersonQuery.Count()}, {nameof(mainCharactersQuery)}: {mainCharactersQuery.Count()}, {nameof(locationQuery)}, {locationQuery.Count()}");

foreach (var mainCharacter in mainCharactersPersonQuery.ToList())
{
    string slotId = $"idle_in_stables";
    var slot = new ScheduleSlot(slotId, 13, ActionNames.SelfCare, "stables");
    mainCharacter.ReceiveEvent(new ScheduleSlotTriggered(startNow.AddDays(1), mainCharacter.Id, slotId, ActionNames.SelfCare, "stables", 0.65));

    if (mainCharacter.Snapshot.Schedule.Occupation is null)
    {
        mainCharacter.ChangeOccupation("farmer");
    }

    Console.WriteLine("Slot: {0}", slotId);
}

var orchestratorLogger = runtime.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DefaultSceneOrchestrator>();
var mainSceneOrchestrator = new DefaultSceneOrchestrator(attractionCalculator, locationService, perceptionPolicy, perceptionOptions, lodRuntime, worldMap, speedProvider, rng, orchestratorLogger);
var bgSceneOrchestrator = new DefaultSceneOrchestrator(attractionCalculator, locationService, perceptionPolicy, perceptionOptions, lodRuntime, worldMap, speedProvider, rng, orchestratorLogger);

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
        mainSceneOrchestrator.OnTick(now, chars);

        HandleChildBornEvents(now, chars, familyGraph, manager, gf, locationService, runtime.Services, mainSceneOrchestrator);

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

    characters.Add(character);
}

if (characters.Count > 0)
{
    var ocLocations = worldMap.GetLocationsInRegion("Village");

    foreach (var character in characters)
    {
        if (character.Snapshot.InteractionSurface.Location == "Unknown")
        {
            locationService.MoveCharacter(character.Id, ocLocations[rng.Next(0, ocLocations.Count)]);
        }
    }

    clock.SetNow(startNow);

    var otherCharactersScene = new SimulationScene(clock, new SimulationSceneOptions
    {
        Characters = characters,
        LocationService = locationService,
        TickStep = WTimeSpan.FromHours(2),
        AstroConfig = astroOptions,
        UniverseConfig = universeOptions,
        SimulationDays = simulationDays,
        DefaultCharacterLod = CognitiveResolutionLevel.Background,
        InternalSubstep = WTimeSpan.FromMinutes(30),
        OnTick = (now, chars) =>
        {
            bgSceneOrchestrator.OnTick(now, chars);

            HandleChildBornEvents(now, chars, familyGraph, manager, gf, locationService, runtime.Services, bgSceneOrchestrator);

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
        await File.WriteAllTextAsync(Path.Combine(promptDir, $"{character.Person.Id.Value.ToString()}.txt"), character.ToPortraitPrompt(
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

static void AddDiaryEntry(StringBuilder stringBuilder, string entry)
{
    Console.WriteLine(entry);
    stringBuilder.AppendLine(entry);
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
static void HandleChildBornEvents(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    FamilyGraph familyGraph,
    GameEngineToolsManager manager,
    GeneratedFile gf,
    ILocationService locations,
    IServiceProvider services,
    DefaultSceneOrchestrator sceneOrchestrator)
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
            sceneOrchestrator.InvalidateLocation(motherLocation);
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

#endregion
