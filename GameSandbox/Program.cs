// Program.cs
// Copyright (c) 50PSoftware

using GameEngineTools;
using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines;
using GameEngineTools.Characters.Engines.Attraction;
using GameEngineTools.Characters.Engines.Behavior;
using GameEngineTools.Characters.Engines.Physiology;
using GameEngineTools.Characters.Engines.Relationships;
using GameEngineTools.Characters.Engines.Reputation;
using GameEngineTools.Characters.Engines.Schedule;
using GameEngineTools.Characters.Generation;
using GameEngineTools.Characters.Generation.Portraits;
using GameEngineTools.Characters.Hosting;
using GameEngineTools.Extensions;
using GameEngineTools.FileSystem;
using GameEngineTools.Narrative;
using GameEngineTools.Universe;
using GameEngineTools.World.Core.Astro;
using GameEngineTools.World.Data;
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

var npcFiles = Directory.GetFiles(gf.NPCDirectory);

var significantOther = gf.ImportNPC(new FileInfo(npcFiles.First(so => so.Contains(soid.ToString()))).Name);

var friend = gf.ImportNPC(new FileInfo(npcFiles.First(fr => fr.Contains(friendId.ToString()))).Name);
var friendSO = gf.ImportNPC(new FileInfo(npcFiles.First(fso => fso.Contains(friendSOId.ToString()))).Name);

manager.Characters.AddRange([significantOther, friend, friendSO]);

var playerPerson = player.Person;
var significantOtherPerson = significantOther.Person;
var friendPerson = friend.Person;
var friendSOPerson = friendSO.Person;

var minage = manager.Characters.Min(ch => ch.Person)!;

var startNow = initTicks == defaultTicks ? WDateTime.New(minage.Identity.BirthDate.AddYears(14)) : new WDateTime(initTicks);

clock.SetNow(startNow);

Console.Title = startNow.Date.ToString();

foreach (var filename in npcFiles)
{
    var character = gf.ImportNPC(new FileInfo(filename).Name);
    if (character.Person.Id.Value.Equals(soid) || character.Person.Id.Value.Equals(friendId) || character.Person.Id.Value.Equals(friendSOId))
        continue;

    if (character.Person.Identity.BirthDate > startNow.Date || character.Person.Age < 6)
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
var sceneOrchestratorOptions = configProvider.GetSection("SceneOrchestrator").Get<SceneOrchestratorOptions>() ?? new SceneOrchestratorOptions();

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

var db = runtime.Services.GetRequiredService<SqliteWorldDatabase>();
var worldMap = SqliteWorldMapLoader.Load(db);
var locationService = (DefaultLocationService)runtime.Services.GetRequiredService<ILocationService>();

{
    for (int index = 0; index < 25; index++)
    {
        var locationDescr = new LocationDescriptor($"village_house_{(index + 1):D2}", $"Village House {(index + 1):D2}", 0.2, 0.5, 3, true, LocationType.Rest, TerrainType.Indoor, 0, true, null);
        worldMap.AddLocation(locationDescr, "Village", locationService);

        var villagelocations = worldMap.GetLocationsInRegion("Village");

        // Napoj nově přidaný dům na každou ostatní vesnickou lokaci obousměrnou
        // hranou se symetrickou náhodnou pěší vzdáleností. Každá dvojice se zpracuje
        // jen jednou (později přidané domy navazují na ty dřívější), takže nevznikají
        // duplicitní hrany.
        foreach (var villageLocation in villagelocations)
        {
            if (villageLocation == locationDescr.Id) continue;

            var distanceMeters = rng.Next(6, 120) + rng.NextDouble();
            worldMap.AddConnection(locationDescr.Id, villageLocation, distanceMeters);
            worldMap.AddConnection(villageLocation, locationDescr.Id, distanceMeters);
        }

        Console.WriteLine("Location updated");
    }
}

// ── Cemetery (Village / Public) — where the dead are interred and mourned ─────
{
    var cemetery = new LocationDescriptor("cemetery", "Hřbitov", 0.05, 0.0, 200, false, LocationType.Public, TerrainType.Courtyard, 0, true, null);
    worldMap.AddLocation(cemetery, "Village", locationService);

    foreach (var villageLocation in worldMap.GetLocationsInRegion("Village"))
    {
        if (villageLocation == cemetery.Id) continue;

        var distanceMeters = rng.Next(40, 200) + rng.NextDouble();
        worldMap.AddConnection(cemetery.Id, villageLocation, distanceMeters);
        worldMap.AddConnection(villageLocation, cemetery.Id, distanceMeters);
    }
}

// Route burials + grave visits to the cemetery instead of interring in place.
sceneOrchestratorOptions = sceneOrchestratorOptions with { CemeteryLocationId = "cemetery" };

worldMap.RegisterAllLocations(locationService);
var objectProvider = runtime.Services.GetRequiredService<IWorldObjectProvider>();
var speedProvider = runtime.Services.GetRequiredService<DefaultMovementSpeedProvider>();
var objectRespawner = runtime.Services.GetRequiredService<ObjectRespawnScheduler>();

// ── Tavern (Village / Social) ─────────────────────────────────────────────────
// 35 apples — primary food source for Village NPCs
for (int index = 0; index < 35; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Food, Id = $"apple_{(index + 1):D2}", DisplayName = "Jablko", LocationId = "tavern", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Food, Respawns = true, RespawnMinutes = 15, WeightGrams = 45, Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.5)], NutritionalProfile = new NutritionalProfile(CalorieGain: 18, ProteinGain: 2, IronGain: 3, VitaminDGain: 0, HydrationGain: 18) });
// 20 beer mugs — drink for Village NPCs
for (int index = 0; index < 20; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"beer_{(index + 1):D2}", DisplayName = "Džbán piva", LocationId = "tavern", AmbientNoise = 0, HeatSignature = 0.1, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 20, WeightGrams = 600, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.6)], NutritionalProfile = new NutritionalProfile(CalorieGain: 28, ProteinGain: 3, IronGain: 2, VitaminDGain: 0, HydrationGain: 25) });
// 10 water jugs — also available in tavern
for (int index = 0; index < 100; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"water_tavern_{(index + 1):D2}", DisplayName = "Džbán vody", LocationId = "tavern", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 30, WeightGrams = 500, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.8)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

// ── Market Square (Village / Public) ──────────────────────────────────────────
for (int index = 0; index < 20; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Food, Id = $"bread_market_{(index + 1):D2}", DisplayName = "Bochánek chleba", LocationId = "market_square", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Food, Respawns = true, RespawnMinutes = 60, WeightGrams = 200, Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.6)], NutritionalProfile = new NutritionalProfile(CalorieGain: 48, ProteinGain: 14, IronGain: 8, VitaminDGain: 0, HydrationGain: 5) });
for (int index = 0; index < 10; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"well_market_{(index + 1):D2}", DisplayName = "Studniční voda", LocationId = "market_square", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 10, WeightGrams = 300, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.9)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

// ── Inn Room (Village / Rest) ─────────────────────────────────────────────────
for (int index = 0; index < 5; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"water_inn_{(index + 1):D2}", DisplayName = "Konvice vody", LocationId = "inn_room", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 60, WeightGrams = 400, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.8)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

// ── Castle Hall (Castle / Social) ─────────────────────────────────────────────
for (int index = 0; index < 15; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Food, Id = $"bread_hall_{(index + 1):D2}", DisplayName = "Chléb", LocationId = "castle_hall", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Food, Respawns = true, RespawnMinutes = 60, WeightGrams = 250, Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.55)], NutritionalProfile = new NutritionalProfile(CalorieGain: 48, ProteinGain: 14, IronGain: 8, VitaminDGain: 0, HydrationGain: 5) });
for (int index = 0; index < 10; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Food, Id = $"meat_hall_{(index + 1):D2}", DisplayName = "Pečené maso", LocationId = "castle_hall", AmbientNoise = 0, HeatSignature = 0.2, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Food, Respawns = true, RespawnMinutes = 120, WeightGrams = 350, Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.8)], NutritionalProfile = new NutritionalProfile(CalorieGain: 62, ProteinGain: 45, IronGain: 20, VitaminDGain: 12, HydrationGain: 3) });
for (int index = 0; index < 15; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"wine_hall_{(index + 1):D2}", DisplayName = "Pohár vína", LocationId = "castle_hall", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 45, WeightGrams = 350, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.65)], NutritionalProfile = new NutritionalProfile(CalorieGain: 22, ProteinGain: 1, IronGain: 3, VitaminDGain: 0, HydrationGain: 22) });
for (int index = 0; index < 10; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"water_hall_{(index + 1):D2}", DisplayName = "Džbán vody", LocationId = "castle_hall", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 30, WeightGrams = 500, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.85)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

// ── Library (Castle / Private) ────────────────────────────────────────────────
for (int index = 0; index < 5; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Food, Id = $"fruit_library_{(index + 1):D2}", DisplayName = "Sušené ovoce", LocationId = "library", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Food, Respawns = true, RespawnMinutes = 120, WeightGrams = 80, Affordances = [new WorldObjectAffordance(AffordanceType.Hunger, 0.3)], NutritionalProfile = new NutritionalProfile(CalorieGain: 32, ProteinGain: 4, IronGain: 6, VitaminDGain: 0, HydrationGain: 4) });
for (int index = 0; index < 5; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"tea_library_{(index + 1):D2}", DisplayName = "Šálek bylinkového čaje", LocationId = "library", AmbientNoise = 0, HeatSignature = 0.3, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 60, WeightGrams = 200, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.6), new WorldObjectAffordance(AffordanceType.MoodBoost, 0.15)], NutritionalProfile = new NutritionalProfile(CalorieGain: 3, ProteinGain: 0, IronGain: 3, VitaminDGain: 0, HydrationGain: 62) });

// ── Courtyard (Castle / Public) ───────────────────────────────────────────────
for (int index = 0; index < 250; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"water_courtyard_{(index + 1):D2}", DisplayName = "Voda ze studny", LocationId = "courtyard", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 10, WeightGrams = 300, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.9)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

// ── Throne Room (Castle / Social) ─────────────────────────────────────────────
for (int index = 0; index < 8; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"wine_throne_{(index + 1):D2}", DisplayName = "Karafa vína", LocationId = "throne_room", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 90, WeightGrams = 700, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.7)], NutritionalProfile = new NutritionalProfile(CalorieGain: 25, ProteinGain: 2, IronGain: 4, VitaminDGain: 0, HydrationGain: 25) });

// ── Stables (Castle / Work) ───────────────────────────────────────────────────
for (int index = 0; index < 5; index++)
    objectProvider.AddObject(new WorldObject { Category = WorldObjectCategory.Drink, Id = $"water_stables_{(index + 1):D2}", DisplayName = "Kbelík vody", LocationId = "stables", AmbientNoise = 0, HeatSignature = 0, IsAvailable = true, IsPickable = true, BlocksLineOfSight = false, ItemKind = PickupItemKind.Drink, Respawns = true, RespawnMinutes = 45, WeightGrams = 400, Affordances = [new WorldObjectAffordance(AffordanceType.Thirst, 0.75)], NutritionalProfile = new NutritionalProfile(CalorieGain: 0, ProteinGain: 0, IronGain: 0, VitaminDGain: 0, HydrationGain: 80) });

var mainCharactersLocations = worldMap.GetLocationsInRegion("Castle");

var mainCharactersQuery = from mainCharacters in manager.Characters
                          //where mainCharacters.Person.Id.Value == playerPerson.Id.Value || mainCharacters.Person.Id.Value == soid || mainCharacters.Person.Id.Value == friendId || mainCharacters.Person.Id.Value == friendSOId
                          select mainCharacters;

var mainCharactersPersonQuery = from mainCharacters in mainCharactersQuery
                                select mainCharacters.Person;

var unknownLocationQuery = from locations in mainCharactersPersonQuery
                    where locations.Snapshot.InteractionSurface.Location == "Unknown"
                    select locations;

var locationQuery = from locations in mainCharactersPersonQuery
                    where locations.Snapshot.InteractionSurface.Location != "Unknown"
                    select locations;

var hlq = from locs in worldMap.GetLocationsInRegion("Village")
          select locs;

var homeLocationsIds = hlq.Where(loc => loc.Contains("house")).ToList();

foreach (var personToMove in unknownLocationQuery)
{
    var homeLocationId = homeLocationsIds[rng.Next(0, homeLocationsIds.Count)];
    var startLocationId = mainCharactersLocations[rng.Next(0, mainCharactersLocations.Count)];
    locationService.MoveCharacter(personToMove.Id, startLocationId);
    personToMove.SetHomeLocation(homeLocationId);
}

foreach (var personToMove in locationQuery)
{
    locationService.MoveCharacter(personToMove.Id, personToMove.Snapshot.InteractionSurface.Location);
}

Console.WriteLine($"{nameof(mainCharactersPersonQuery)}: {mainCharactersPersonQuery.Count()}, {nameof(mainCharactersQuery)}: {mainCharactersQuery.Count()}, {nameof(unknownLocationQuery)}, {unknownLocationQuery.Count()}");

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

Console.WriteLine("Press any key to continue...");

var orchestratorLogger = runtime.Services.GetRequiredService<ILoggerFactory>().CreateLogger<DefaultSceneOrchestrator>();
var reputationLedger = runtime.Services.GetRequiredService<CommunityReputationLedger>();
var statusLedger = runtime.Services.GetRequiredService<GameEngineTools.Characters.Engines.Status.StatusLedger>();

// Ascribed status: give a few occupations a conferred-rank prior (radní/vedoucí/starší) that the
// StatusLedger blends with the emergent consensus. Most occupations are commoners (no prior).
var ascribedStatus = runtime.Services.GetRequiredService<GameEngineTools.Characters.Engines.Status.DefaultAscribedStatusProvider>();
foreach (var ch in manager.Characters)
{
    var role = ch.Person.Snapshot.Schedule?.Occupation switch
    {
        "scholar" => GameEngineTools.Characters.Engines.Status.AscribedRole.Leader,
        "merchant" or "guard" => GameEngineTools.Characters.Engines.Status.AscribedRole.Official,
        "healer" => GameEngineTools.Characters.Engines.Status.AscribedRole.Elder,
        _ => GameEngineTools.Characters.Engines.Status.AscribedRole.Commoner,
    };
    ascribedStatus.SetRole(ch.Person.Id, role);
}

var mutableObjectProvider = runtime.Services.GetService<GameEngineTools.World.Objects.IMutableWorldObjectProvider>();
var mainSceneOrchestrator = new DefaultSceneOrchestrator(attractionCalculator, locationService, perceptionPolicy, perceptionOptions, lodRuntime, worldMap, speedProvider, rng, orchestratorLogger, objectProvider, sceneOrchestratorOptions, reputationLedger, statusLedger, mutableObjectProvider);
//var bgSceneOrchestrator = new DefaultSceneOrchestrator(attractionCalculator, locationService, perceptionPolicy, perceptionOptions, lodRuntime, worldMap, speedProvider, rng, orchestratorLogger, objectProvider, sceneOrchestratorOptions, reputationLedger);

var writeBuffer = runtime.Services.GetRequiredService<WorldObjectWriteBuffer>();
var objectSnapshotCache = runtime.Services.GetRequiredService<WorldObjectSnapshotCache>();

Console.ReadKey();

var mainCharactersSceneOpts = new SimulationSceneOptions
{
    //Characters = [playerPerson, significantOtherPerson, friendPerson, friendSOPerson],
    Characters = manager.Characters.Select(p => p.Person).ToList(),
    LocationService = locationService,
    AstroConfig = astroOptions,
    UniverseConfig = universeOptions,
    SimulationDays = simulationDays,
    TickStep = WTimeSpan.FromHours(0.5),
    InternalSubstep = WTimeSpan.FromMinutes(5),
    NarrativeFormatter = new DefaultNarrativeFormatter(),
    ObjectSnapshotCache = objectSnapshotCache,
    WriteBuffer = writeBuffer,
    RespawnScheduler = objectRespawner,
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

AuditConsumableRouting(objectProvider, locationService);

await mainCharactersScene.RunAsync();

//var characters = new List<IHuman>();

//foreach (var character in manager.Characters.Select(c => c.Person).ToList())
//{
//    var mainCharacters = mainCharactersPersonQuery.ToList();
//    if (mainCharacters.Contains(character))
//    {
//        continue;
//    }

//    characters.Add(character);
//}

//if (characters.Count > 0)
//{
//    var ocLocations = worldMap.GetLocationsInRegion("Village");

//    foreach (var character in characters)
//    {
//        if (character.Snapshot.InteractionSurface.Location == "Unknown")
//        {
//            locationService.MoveCharacter(character.Id, ocLocations[rng.Next(0, ocLocations.Count)]);
//        }
//        else
//        {
//            locationService.MoveCharacter(character.Id, character.Snapshot.InteractionSurface.Location);
//        }
//    }

//    clock.SetNow(startNow);

//    var otherCharactersScene = new SimulationScene(clock, new SimulationSceneOptions
//    {
//        Characters = characters,
//        LocationService = locationService,
//        TickStep = WTimeSpan.FromHours(2),
//        AstroConfig = astroOptions,
//        UniverseConfig = universeOptions,
//        SimulationDays = simulationDays,
//        ObjectSnapshotCache = objectSnapshotCache,
//        WriteBuffer = writeBuffer,
//        RespawnScheduler = objectRespawner,
//        DefaultCharacterLod = CognitiveResolutionLevel.Background,
//        InternalSubstep = WTimeSpan.FromMinutes(30),
//        OnTick = (now, chars) =>
//        {
//            bgSceneOrchestrator.OnTick(now, chars);

//            HandleChildBornEvents(now, chars, familyGraph, manager, gf, locationService, runtime.Services, bgSceneOrchestrator);

//            Console.Title = now.Date.ToString();
//        }
//    }, lodRuntime);

//    await otherCharactersScene.RunAsync();
//}

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
    //Console.WriteLine(entry);
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

#region Diagnostics — consumable routing audit

/// <summary>
/// Audits whether <c>MoveTo:Food</c> / <c>MoveTo:Drink</c> routing can ever succeed,
/// by reproducing the exact two filters used inside
/// <see cref="DefaultSceneOrchestrator"/>.<c>FindLocationWithCategory</c>:
/// <list type="number">
///   <item><c>IsAvailable == true</c> (filter A)</item>
///   <item>the object's <c>LocationId</c> is registered in <see cref="ILocationService"/>
///         (otherwise <c>MoveCharacter</c> would throw)</item>
/// </list>
/// Prints Food and Drink side by side so the working category (Drink) can be
/// compared against the broken one (Food).
/// </summary>
/// <param name="objectProvider">The same provider instance passed to the orchestrator.</param>
/// <param name="locationService">The same location service the scene uses.</param>
static void AuditConsumableRouting(
    IWorldObjectProvider objectProvider,
    ILocationService locationService)
{
    // Snapshot all objects once — GetAllObjects() bypasses the per-tick cache and
    // hits the backing SQLite provider, exactly like FindLocationWithCategory does.
    var allObjects = objectProvider.GetAllObjects().ToList();

    Console.WriteLine();
    Console.WriteLine("==== CONSUMABLE ROUTING AUDIT ====");
    Console.WriteLine($"Total world objects seen by provider: {allObjects.Count}");

    foreach (var category in new[] { WorldObjectCategory.Food, WorldObjectCategory.Drink })
    {
        var ofCategory = allObjects
            .Where(o => o.Category == category)
            .ToList();

        // Reproduce filter A: only available objects are routable.
        var available = ofCategory
            .Where(o => o.IsAvailable)
            .ToList();

        // Distinct locations holding at least one AVAILABLE object of this category.
        var availableLocations = available
            .Select(o => o.LocationId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Reproduce filter B: a location is only a valid MoveCharacter target
        // when it is registered (GetDescriptor returns non-null).
        var registered = availableLocations
            .Where(loc => locationService.GetDescriptor(loc) is not null)
            .ToList();

        var unregistered = availableLocations
            .Where(loc => locationService.GetDescriptor(loc) is null)
            .ToList();

        Console.WriteLine();
        Console.WriteLine($"--- {category} ---");
        Console.WriteLine($"  rows in DB:                 {ofCategory.Count}");
        Console.WriteLine($"  IsAvailable == true:        {available.Count}");
        Console.WriteLine($"  held by someone (HeldBy):   {ofCategory.Count(o => o.HeldBy is not null)}");
        Console.WriteLine($"  distinct available locations: {availableLocations.Count}");
        Console.WriteLine($"    registered in LocationService:   {registered.Count}  [{string.Join(", ", registered)}]");
        Console.WriteLine($"    NOT registered (MoveCharacter would throw): {unregistered.Count}  [{string.Join(", ", unregistered)}]");

        // Verdict: routing can only ever succeed if at least one available object
        // sits at a registered location.
        var routable = registered.Count > 0;
        Console.ForegroundColor = routable ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine($"  => MoveTo:{category} CAN resolve a destination: {routable}");
        Console.ResetColor();
    }

    Console.WriteLine("==================================");
    Console.WriteLine();

    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
}

#endregion Diagnostics — consumable routing audit

#region Diagnostics — MoveTo outbox probe

/// <summary>
/// One-shot runtime probe placed in the scene's OnTick, BEFORE the orchestrator runs.
/// Reveals whether an <see cref="ActionCommitted"/> for <c>MoveTo:Food</c> / <c>MoveTo:Drink</c>
/// is actually present in <see cref="IHuman.LastOutbox"/> at the moment
/// <see cref="DefaultSceneOrchestrator.OnTick"/> reads it — and whether the character's
/// location changes across probes.
/// </summary>
/// <remarks>
/// Splits the problem cleanly:
/// <list type="bullet">
///   <item>MoveTo IS in LastOutbox but location never changes → break is INSIDE RouteMoveTo
///         (destination resolution or MoveCharacter).</item>
///   <item>MoveTo is NEVER in LastOutbox here → break is a timing/outbox issue
///         (the committed event is not visible to OnTick).</item>
/// </list>
/// </remarks>
/// <param name="chars">Characters about to be processed by the orchestrator.</param>
/// <param name="locations">Location service — used to read each character's current location.</param>
/// <param name="objectProvider">Provider — used to check whether food is co-located.</param>
static void ProbeMoveToOutbox(
    IReadOnlyList<IHuman> chars,
    ILocationService locations,
    IWorldObjectProvider objectProvider)
{
    foreach (var character in chars)
    {
        // Find any committed MoveTo:* action sitting in the outbox right now.
        var moveTo = character.LastOutbox
            .OfType<ActionCommitted>()
            .FirstOrDefault(a => a.ActionName.StartsWith("MoveTo:Food", StringComparison.OrdinalIgnoreCase));

        if (moveTo is null)
            continue; // Only log when the character is actually requesting a move.

        var loc = locations.GetLocation(character.Id) ?? "<unplaced>";

        // Does the current location already have an available object of the requested category?
        var hasFoodHere = objectProvider.GetObjectsAt(loc)
            .Any(o => o.Category == WorldObjectCategory.Food && o.IsAvailable);
        var hasDrinkHere = objectProvider.GetObjectsAt(loc)
            .Any(o => o.Category == WorldObjectCategory.Drink && o.IsAvailable);

        Console.WriteLine(
            "[PROBE] {0} outbox has '{1}' | currentLoc={2} | foodHere={3} drinkHere={4}",
            character.Id.Value.ToString()[..8],
            moveTo.ActionName,
            loc,
            hasFoodHere,
            hasDrinkHere);
    }
}

#endregion Diagnostics — MoveTo outbox probe

#region Diagnostics — MoveTo:Food destination trace

/// <summary>
/// Wraps the orchestrator call to trace exactly where a <c>MoveTo:Food</c> request
/// sends each character. Captures location before and after
/// <see cref="DefaultSceneOrchestrator.OnTick"/>, prints the expected destination
/// (replicating <c>FindLocationWithCategory</c>'s provider scan), and dumps the
/// character's Food memory facts (what the memory-based router sees first).
/// </summary>
/// <param name="now">Current simulation time.</param>
/// <param name="chars">Scene characters.</param>
/// <param name="orchestrator">The orchestrator to invoke.</param>
/// <param name="locations">Location service for before/after readings.</param>
/// <param name="objectProvider">Provider — replicates the expected food destination.</param>
static void TraceFoodMove(
    WDateTime now,
    IReadOnlyList<IHuman> chars,
    DefaultSceneOrchestrator orchestrator,
    ILocationService locations,
    IWorldObjectProvider objectProvider)
{
    // 1) Capture pre-orchestrator location for every character that wants food.
    var before = new Dictionary<HumanId, string>();
    foreach (var character in chars)
    {
        var wantsFood = character.LastOutbox
            .OfType<ActionCommitted>()
            .Any(a => a.ActionName == ActionNames.MoveToFood);

        if (wantsFood)
            before[character.Id] = locations.GetLocation(character.Id) ?? "<none>";
    }

    // 2) Run the orchestrator (this is where RouteMoveTo executes).
    orchestrator.OnTick(now, chars);

    // 3) Report before -> after, the expected destination, and memory facts.
    foreach (var (id, fromLoc) in before)
    {
        var afterLoc = locations.GetLocation(id) ?? "<none>";

        // What FindLocationWithCategory SHOULD return: any available Food object
        // at a location other than the current one (fallback path).
        var expectedCandidates = objectProvider.GetAllObjects()
            .Where(o => o.Category == WorldObjectCategory.Food
                     && o.IsAvailable
                     && o.LocationId != fromLoc)
            .Select(o => o.LocationId)
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToList();

        // What the memory-based router sees: remembered Food facts.
        var human = chars.First(c => c.Id == id);
        var foodMemories = human.Snapshot.Memory.KnownObjects
            .Where(f => f.ItemKind == PickupItemKind.Food)
            .Select(f => $"{f.LocationId}(conf={f.Confidence:F2})")
            .Take(5)
            .ToList();

        // What memory thinks is DRINK — to detect mis-tagging (Hypothesis A).
        var drinkMemories = human.Snapshot.Memory.KnownObjects
            .Where(f => f.ItemKind == PickupItemKind.Drink)
            .Select(f => $"{f.LocationId}(conf={f.Confidence:F2})")
            .Take(5)
            .ToList();

        Console.WriteLine(
            "[FOODMOVE] {0} | {1} -> {2} (changed={3}) | expectedFoodDest=[{4}] | foodMem=[{5}] | drinkMem=[{6}]",
            id.Value.ToString()[..8],
            fromLoc,
            afterLoc,
            fromLoc != afterLoc,
            string.Join(", ", expectedCandidates),
            string.Join(", ", foodMemories),
            string.Join(", ", drinkMemories));
    }
}

#endregion Diagnostics — MoveTo:Food destination trace