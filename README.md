# GameEngineTools (GET)

> **A white-box social/physical simulation stack — C# / .NET 8**
> © 50PSoftware

![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

This repository is a multi-tool solution, not a single engine. At its core is **`GameEngineTools`**,
a research-grade autonomous-NPC simulation library that exposes the full internal state of every
character (physiology, psychology, memory, relationships, values, identity, social standing) instead
of hiding it behind a black box. Around that core sit standalone command-line generators for terrain
and world content, a realtime browser dashboard, and a couple of viewer/authoring apps used while
building all of the above.

---

## Projects in this repository

| Project | Kind | What it does |
|---|---|---|
| **`GameEngineTools/`** | Library | The NPC simulation engine — see [below](#get-core-the-npc-simulation-engine). |
| **`EngineTests/`** | MSTest suite | Unit tests for the core library. |
| **`GameSandbox/`** | Console app | Canonical, fully-wired example that runs a `SimulationScene` end-to-end and prints a Czech-language diary. |
| **`CharacterGenerator/`** | Console app | Interactive CLI for generating and inspecting single characters/families without running a full scene. |
| **`TerraGen/`** | Console app | Procedural planet **terrain** generator — noise, tectonic plates, hydraulic/SPIM erosion, rivers, isostasy, orographic precipitation, Köppen-Geiger climate; writes heightmap tiles to a SQLite `terrain.db`. (test suite: `TerraGenTests/`) |
| **`WorldGen/`** | Console app | Procedural **world content** generator that runs on top of a TerraGen `terrain.db` — places settlements, roads, and locations following the terrain (rivers, coastline, slope) and writes `world.db`. (test suite: `WorldGenTests/`) |
| **`WorldObserver/`** | ASP.NET Core + SignalR app | Realtime browser dashboard that runs a live `GameEngineTools` simulation and streams character/world state to the browser (cards, relationship graph, map, charts, playback controls). |
| **`LogsResolver/`** | WPF app | Desktop JSONL log viewer for simulation logs. (test suite: `LogsResolverTests/`) |
| **`TerrainEditor/`** | WPF app | Desktop viewer for TerraGen/WorldGen output — inspect heightmaps, rivers, roads, and locations produced by those tools. (test suite: `TerrainEditorTests/`) |
| **`RelationshipsGame/`** | WPF app | Early-stage prototype UI built on the relationships/attraction engines. |

The two data-generation tools chain together: **TerraGen** produces terrain →
**WorldGen** places settlements/roads/locations on that terrain → **GameSandbox** /
**WorldObserver** populate the resulting world with `GameEngineTools` characters and simulate them.

---

## GET Core: the NPC simulation engine

GET's character engine runs dozens of autonomous NPCs that eat, sleep, work to a schedule, move
between locations, form and decay relationships, gossip, grieve, fall in love, marry, have children,
age, and die — with no scripted behavior trees. Every character perceives the world as **structured
semantic data**, never pixels, and every character exposes an immutable `EnginesSnapshot` with the
live state of every engine — nothing is hidden.

### What you can do with it

- **Simulate a living social world.** `SimulationScene` ticks a roster of characters, routes their
  interactions, applies celestial/ambient context, and produces a narrative diary.
- **Inspect everything.** PAD affect, stress/cortisol, needs, goals, beliefs about other people,
  values, self-esteem, social status, grief, economic wealth — all readable off the snapshot.
- **Generate believable people.** Deterministic, seedable generation of Big Five personality,
  appearance/genetics, attraction preferences, values, interests, and whole nuclear families with
  genetically-inherited children.
- **Drive psychology from the body and the world.** Pain, hunger, fever, sleep debt, ambient
  temperature, noise, crowding, privacy, daylight, and seasons all feed affect and decision-making.
- **Model real planetary mechanics.** An optional Kepler orbital stack drives day length, seasons,
  irradiance, ambient temperature, and gravity from a configurable star/planet/moon/ring system.
- **Run a food & money economy.** Characters produce, carry, buy, sell, and eat food with spoilage;
  wages and posted prices are tracked in a persistent economy ledger.
- **Model death and its aftermath.** Bereavement (grief decay, widowhood hazard), burial (graves as
  persistent world objects), and grave visits are first-class, behavior-driven events.
- **Model social standing.** A two-axis (Dominance/Prestige) status ledger and a community reputation
  ledger shape deference, stress, and the trust prior a stranger starts from.
- **Talk.** Characters plan and realize actual Czech sentences (not templated barks) via speech acts,
  a per-character acquired vocabulary, and a full valency/sentence-planning pipeline.
- **Persist and resume.** Characters serialize to/from JSON snapshots; world objects, the map, the
  economy ledger, and social norms persist in SQLite.
- **Scale with LOD.** Per-character cognitive-resolution tiers (Player / Nearby / Background) control
  how often each character reasons and at what fidelity — so background crowds stay cheap.

### Quick start

The fastest path to a running world is `GameEngineToolsRuntime`, which builds the DI container,
configures the world clock/calendar, registers every engine and the generation pipeline, and returns
a handle:

```csharp
using GameEngineTools.Characters.Hosting;
using GameEngineTools.World.Simulation;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.DependencyInjection;

// 1. Start the runtime (DI, world clock, engines, generation, logging).
await using var runtime = await GameEngineToolsRuntime.StartAsync(consoleLogs: true);

var manager  = runtime.GameEngineToolsManager;   // character roster + generation helpers
var clock    = runtime.Clock;                    // world time source
var services = runtime.Services;                 // full IServiceProvider
var lodRuntime = services.GetRequiredService<ICognitiveResolutionLevelRuntime>();

// 2. Generate a few characters (RandomizePerson returns a live IHuman).
var roster = new List<IHuman>();
for (int i = 0; i < 8; i++)
    roster.Add(manager.RandomizePerson(maxAge: 60, sexBiology: null, minAge: 18));

// 3. Build a scene and run it for N in-game days.
var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters     = roster,
    SimulationDays = 20,
    TickStep       = WTimeSpan.FromHours(0.5),
}, lodRuntime);

await scene.RunAsync();
```

> Configuration is read from `appsettings.*.json` (character engines, world clock, astronomy).
> `GameSandbox/Program.cs` is the canonical, fully-wired reference runner.

If you want to assemble the container yourself instead of using the runtime, register everything via
DI directly — see [DI Registration](#di-registration).

### Usage recipes

**Run a scene.** `SimulationScene` owns the clock, ticks every character through the full pipeline in
list order, routes interaction outcomes between characters, injects ambient/celestial context, runs
the narrative formatter, and applies LOD:

```csharp
var opts = new SimulationSceneOptions
{
    Characters      = roster,                       // tick order = list order (player at index 0 by convention)
    LocationService = locationService,               // enables ContextChanged dispatch + InteractionSurface
    SimulationDays  = 30,
    TickStep        = WTimeSpan.FromHours(0.5),
    InternalSubstep = WTimeSpan.FromMinutes(5),      // finer character-to-character latency
    AstroConfig     = astroConfig,                   // sun model → ambient temperature & daylight
    UniverseConfig  = universeConfig,                // full Kepler planetary mechanics

    NarrativeFormatter = new DefaultNarrativeFormatter(),
    ResolveCharacter   = id => new NarrativeCharacterInfo(name, biology),
    OnNarrative        = entry => diary.Add(entry),

    DefaultCharacterLod = CognitiveResolutionLevel.Nearby,
    ResolveCharacterLod = ch => SceneCharacterLodResolver.Resolve(ch, playerId, locationService, hotSet),

    OnTick = (now, chars) => { /* scene logic: route ReachOut, handle ChildBorn, etc. */ },
};

await new SimulationScene(clock, opts, lodRuntime).RunAsync();
```

**Generate characters & families.** Generation is deterministic when seeded and produces a
`HumanBlueprint` (+ immutable `GeneticBlueprint`) that `DefaultHumanFactory` turns into a live
`OrchestratedHuman`:

```csharp
// Single random person (uses the registered HumanBlueprintSpec).
IHuman person = manager.RandomizePerson();
IHuman young  = manager.RandomizePerson(maxAge: 25, sexBiology: SexBiology.Female, minAge: 18);

// A whole nuclear family with genetically-inherited children (requires AddFamilySystem()).
var familyGen = services.GetRequiredService<NuclearFamilyGenerator>();
var familyGraph = services.GetRequiredService<FamilyGraph>();
NuclearFamily family = familyGen.Generate(new NuclearFamilySpec(/* … */), familyGraph, clock.Now);
```

**Read a character's state.** Everything observable lives on the snapshot — read it directly, no
reflection:

```csharp
var s = person.Snapshot;

double stress   = s.Psychology.Stress;           // HPA-axis 0..100
var    emotion  = s.Psychology.DominantEmotion;  // Joy, Anger, Shame, …
double hunger   = s.Physiology.Hunger;
var    intent   = s.Behavior.ActiveIntent;       // current stabilized direction
var    goals    = s.Goals?.Active;               // persistent long-term drives
var    values   = s.Values?.Current;             // drifting Schwartz profile
double esteem   = s.SelfConcept?.SelfEsteem ?? 0.5;

// Beliefs this character holds about someone else:
if (s.SemanticMemory?.GetBeliefs(otherId) is { } beliefs)
    Console.WriteLine($"Warm={beliefs.StrengthOf(PersonBeliefKind.Warm)} " +
                      $"Rejecting={beliefs.StrengthOf(PersonBeliefKind.Rejecting)}");

// A directed relationship edge:
s.Relationships.Deconstruct(out var edges);
if (edges.TryGetValue(otherId, out var edge))
    Console.WriteLine($"Trust={edge.Trust} Closeness={edge.Closeness} Like={edge.Like}");

// Events the character emitted on its most recent tick:
foreach (var ev in person.LastOutbox) { /* … */ }
```

**Drive a character with events.** Characters react to external stimuli delivered through the inbox
(processed in Phase A of the next tick), or immediately via `FlushInbox()` at setup time:

```csharp
person.ReceiveEvent(new ScheduleSlotTriggered(now, person.Id, slotId, ActionNames.SelfCare, "stables", 0.65));
person.SetHomeLocation("house_03");
person.ChangeOccupation("farmer");   // re-seeds the daily schedule
person.SetLastName(partner);         // e.g. on marriage
```

Outside a scene you can also tick a character manually: `person.Tick(now, dt)`.

**Schedules & occupations.** A character's day is driven by an occupation looked up in
`IOccupationRegistry` (built-ins plus custom rows from `SourceFiles/Characters/Occupations.csv`).
Each `ScheduleSlot` biases a preferred action (and optionally a `MoveTo` toward a location) at a given
hour, and can be skipped under stress. Occupation schedules drive commuting (`MoveTo:*`) between home
and workplace.

**World, locations & objects.**

```csharp
var locationService = new DefaultLocationService(socialNormProvider);
worldMap.RegisterAllLocations(locationService);        // bulk-register from CSV/SQLite
locationService.MoveCharacter(person.Id, "tavern_01"); // updates InteractionSurface next tick
```

`LocationDescriptor` carries noise, crowding, capacity, privacy, and type; the location service
computes a per-tick `InteractionSurface` (noise, crowding, privacy, proxemics) and dispatches
`ContextChanged` only to characters that moved. **World objects** (`WorldObject`) are perceived as a
category + a list of affordances; a character that uses one emits `ObjectAffordanceApplied`, which
Physiology/Psychology consume (e.g. a fireplace warms; a bench rests). A hard/soft affordance gate
enforces object presence (`Eat` needs `Food`, `Work` needs `Tool`) and can redirect a gated-out action
into a `MoveTo:Food`/`MoveTo:Drink` foraging move. Objects — including graves and priced shop goods —
persist in SQLite and can respawn on a schedule.

**Astronomy & seasons.** Supply an `AstroConfig` (and optionally a `UniverseConfig`) to a scene and
each tick gets a `CelestialContext` — irradiance, day length, sunrise/sunset, season, and ambient
temperature. With a `UniverseConfig`, the `Universe/` Kepler stack (`KeplerSolver`, `OrbitalElements`,
`StarPhysics`, `MoonPhysics`, `RingSystem`, `HabitabilityProfile`) derives those from real orbital
mechanics for a configurable star/planet/moon/ring system.

**Persistence.**

```csharp
var gf = (GeneratedFile)services.GetRequiredService<IGeneratedFile>();
gf.Export(npc);                                  // write JSON snapshot (overloads: Export(PC) / Export(NPC))
NPC restored = gf.ImportNPC("npc_<guid>.jsonl"); // CharacterBase; restored.Person is the IHuman
// A character can also reload state in place:
restored.Person.RestoreSnapshot(snapshot, today); // revalidates age-dependent subsystems
```

`Characters/Persistence/` handles JSON (de)serialisation of `EnginesSnapshot`. Newer engine fields are
nullable for backward compatibility with older saves.

**Level of detail (LOD).** `CognitiveResolutionLevel` (Player / Nearby / Background) controls
**decision cadence** (how often Behavior reasons, via `IBehaviorCadencePolicy` + `Characters:Lod`) and
**fidelity** of memory, perception, and social processing (`Characters:Fidelity`). Resolve per
character with `ResolveCharacterLod`; background crowds reason hourly at reduced fidelity while the
player reasons every few minutes at full detail.

### Architecture

Each NPC is an **`OrchestratedHuman`**. Engines never call each other directly — they **read** the
shared per-tick `EnginesSnapshot` through `IHumanContext` and **emit** `IDomainEvent`s into an outbox
that the orchestrator drains and routes. Every engine implements the same contract
(`IEngine<TState, TConfig>`): `State`, `Config`, `Tick`, `Handle`, `RestoreState`.

```
Phase A  ──  HandleScheduled + HandleInbox
             (scheduled actions + external events delivered against the PREVIOUS snapshot)

Phase B  ──  [LifeStage boundary check]
             Physiology → Psychology → [mid-tick snapshot refresh]
             → Behavior (cadence-gated) → Interactions → ObjectInteraction
             → Relationships → Memory → SemanticMemory
             → Goals → Schedule → Values → SelfConcept → Interests
             → Bereavement → Status → Economy → Social Comparison
             [final snapshot refresh]

Phase C  ──  SelfDeliver (≤ 8 passes)  →  snapshot refresh  →  PublishOutbox
             (character reacts to its own Phase-B events)
```

Invariants:

- **Order is load-bearing.** Physiology and Psychology run first; a **mid-tick snapshot refresh after
  Psychology** lets Behavior read the *current* tick's physio/psych state.
- **Behavior runs on a cadence** (LOD), while physiology/psychology/memory always advance with world
  time.
- **Death is terminal** — a dead character runs no further engines but stays in the roster (and can
  trigger bereavement/burial in survivors).
- **Action slots** (`ActiveActionSlots`) track occupied body/mind channels so Behavior can model
  multitasking instead of committing impossible action combinations.

### Engine reference

| Engine | State | What it owns |
|---|---|---|
| **Physiology** | `PhysiologyState` | Energy/hunger/thirst/pain/immune/temperature, sleep debt, allostatic load, cortisol, testosterone, nutrition (vitamin D/iron), menstrual cycle, aging, injury, postpartum, mortality. Emits `ChildBorn`, `InjuryReceived`, death. |
| **Psychology** | `PsychologyState` | PAD affect, stress (HPA), cognitive load, cortisol, mood baseline, discrete emotions + decay, circadian arousal, hormonal/environmental/sickness modulation, stress manifestation. Anger is approach-motivated. |
| **Behavior** | `BehaviorState` | Decision core: need engines (physiological/social/competence/autonomy/foraging) + modifier engines (trait/affect/circadian/habit/memory/affordance/values/goal/schedule) + intent stabilisation + action arbitration + habit learning. Emits `ActionCommitted`, `InteractionProposed`. |
| **Sleep** | `ISleepSession` | `Falling→Light→Deep→REM→Waking` state machine; nightmares, ambush, consolidation; outside the utility loop. |
| **Interactions** | `InteractionSurface` | Evaluates proposed social acts (`SpeechAct`s, touch levels); misattribution under noise×stress; peak-end valence; sexual-encounter readiness gate; third-party observers. |
| **Dialogue / Language** | — | Turns a chosen speech act into an actual realized Czech sentence via `CzechSpeechActRealizer` (valency/sentence-planning pipeline) and a per-character acquired vocabulary (`LexicalAcquisition`). |
| **Object Interaction** | — | Applies world-object affordances; pickup/ownership routing. |
| **Relationships** | `RelationshipState` | Asymmetric directed graph: like/trust/closeness/respect/comfort/familiarity, attraction dimensions, communal vs exchange strength, investment, transgression residue + repair, Navarro & Dunbar decay, attachment modulation. |
| **Memory** | `MemoryIndex` | Episodic encode/recall/forget: Ebbinghaus decay, spacing, peak-end salience, reconsolidation drift, stress distortion, System-1/2 switching; knowledge facts with confidence. |
| **Semantic Memory** | `SemanticMemoryState` | Per-person belief sets (Warm/EmotionallySafe/Reliable/Rejecting/Critical) distilled from episodes; attachment-modulated learning; feeds social targeting. |
| **Goals** | `GoalState` | Persistent long-term drives (existential/survival/career/relational) with salience/progress/frustration; bias utility, don't prescribe plans. |
| **Daily Schedule** | `DailyScheduleState` | Occupation-seeded time-of-day routine slots; biases action + movement; runtime occupation change. |
| **Values** | `ValuesState` | Drifting Schwartz `Current` vs immutable `Baseline`; congruence shifts utility & emits guilt on violation. |
| **Self-Concept** | `SelfConcept` | Perceived Big Five, ideal subset, self-esteem, self-discrepancy; evolves via self-verification; seeds `BuildIdentity` goals. |
| **Interests** | `InterestState` | Drifting RIASEC `Current` vs immutable `Baseline`; rewarded activity raises matching interest. |
| **Bereavement** | — | Grief (dual-process model) after a death and widowhood hazard; drives `Bury`/`MournAtGrave` behavior. |
| **Status** | `StatusLedger` | Two-axis Dominance/Prestige social standing per character; feeds stress via status×stability and deference in interactions. |
| **Economy** | `EconomyLedger` | Wealth, wages, `Buy`/`Sell` actions, posted prices on `WorldObject`s. |
| **Social Comparison** | — | Contrast/assimilation against comparison targets; benign/malicious envy; downward mood repair. |

### Supporting social systems

Shared math/services, not pipeline engines:

- **Theory of Mind** (`ToM/ToMMath`) — recursive belief reasoning with a per-NPC recursion ceiling
  (mean ≈ 4); `MutualKnowledgeFormed` for common knowledge.
- **Community Reputation** (`Reputation/CommunityReputationLedger`) — a **scene-level singleton** that
  folds observed acts into per-subject reputation with recency weighting, stern-judging negativity
  bias, and community diffusion; yields the trust prior a stranger starts from.
- **Life-Stage Transitions** — `OrchestratedHuman` emits `LifeStageTransitionOccurred` on boundary
  crossings; `LifeStageMath` provides probabilistic reappraisal hooks (e.g. midlife mood dip).
- **Attraction** (`Attraction/DefaultAttractionCalculator`) — pure, stateless, asymmetric: base
  physical + preference match + state modifier + mere-exposure + excitatory transfer, orientation-
  weighted. Called per-pair on demand.

### Traits

The stable, slow-changing layer (`Characters/Traits/`): **Personality** (Big Five), **AttachmentProfile**
(continuous Anxiety×Avoidance ECR-R), **ValuesProfile** (Schwartz), **InterestProfile** (RIASEC),
**PsychologicalProfile**, **SexualResponsiveness** (Dual Control Model SES/SIS), sociosexuality
(SOI-R), **SexualOrientation**, **PhysicalAppearance** / **Morphology** / **AttractionProfile**.

### Configuration

Character config binds from `appsettings.Characters.json` under `Characters:*` via `IOptions<T>`;
`appsettings.Characters.Default.json` is the documented baseline (override per environment). World
and astronomy config bind from `appsettings.World.json` under `World:*`. Each config record lives
beside its engine.

Active `Characters:*` sections include: `Physiology`, `MenstrualCycle`, `Psychology`, `Behavior`,
`Sleep`, `Interactions`, `Relationships`, `Memory`, `SemanticMemory`, `Goals`, `DailySchedule`,
`Values`, `SelfConcept`, `Interests`, `Bereavement`, `Status`, `Economy`, `Lod` (decision cadence per
LOD tier), `Fidelity` (memory/perception/social fidelity per tier).

`World:*` sections: `Perception`, `Astro` (sun model, latitude, seasonal amplitude & thermal lag),
`Universe` (full star/planet/moon/ring definition), and `Calendar` (cultural overlay: month count,
target year length, time subdivisions, leap rule). The world clock/calendar is **derived from
physics** — `PlanetaryCalendarFactory` builds the `WorldTimeSpec` from `World:Universe` (planet
sidereal rotation → hours-per-day, orbit → year length) plus the `World:Calendar` overlay; there is
no separate `InitWorldClock` section. The default template is Earth; the sandbox ships an alternate
"Vigilia Insectianis" world (26-hour day, 10 months, 360-day year, +5 leap days every 4 years).

### DI Registration

`Characters/Hosting/ServiceCollectionExtensions.cs`. The shorthand registers the core pipeline engines
at once; additional engines (Goals, SelfConcept, Interests, Bereavement, Status, Economy, Social
Comparison, Object Interaction) and support services come from `AddCharactersCore` and their own
`Add*Engine()` methods:

```csharp
services.AddCharacters<
    DefaultPhysiologyEngine,
    DefaultPsychologyEngine,
    DefaultBehaviorEngine,
    DefaultInteractionEngine,
    DefaultRelationshipsEngine,
    DefaultMemoryEngine,
    DefaultSemanticMemoryEngine,
    DefaultGoalEngine,
    DefaultDailyScheduleEngine>();

services.AddObjectInteractionEngine();   // optional object-interaction subsystem
services.AddCharacterGeneration(spec);   // or the lazy Func<IServiceProvider, HumanBlueprintSpec> overload
services.AddFamilySystem();              // FamilyGraph + NuclearFamilyGenerator (after AddCharacterGeneration)
```

Each `Add*Engine<T>()` binds its `IOptions<TConfig>` automatically (overridable via a lambda).
`GameEngineToolsRuntime.StartAsync` does all of this for you.

---

## TerraGen & WorldGen: world data generation

**TerraGen** and **WorldGen** are standalone console tools that generate the physical/geographic world
GET characters live in. They run as a pipeline: TerraGen writes a `terrain.db`, WorldGen reads it and
writes a `world.db` next to it.

**TerraGen** (planet terrain):
- Perlin/simplex noise heightmaps calibrated against real-Earth elevation statistics
- Optional tectonic plates (convergent/divergent boundaries drive mountains/rifts) with a configurable
  plate count
- Hydraulic erosion, plus an optional SPIM (stream-power incision model) path with rock-type
  hardness, Airy isostatic rebound, and orographic precipitation
- River network extraction (Montgomery & Dietrich 1992 channel-initiation threshold) as a persisted
  graph, reused by WorldGen's road pathfinder
- Köppen-Geiger climate classification and land/ocean-per-hemisphere climate asymmetry
- `--scan` prints a colored ASCII map of a lat/lon window (elevation, tectonic boundaries, or
  Köppen-Geiger climate); `--scan-levels` automates a multi-resolution scan
- Optional parallel tile generation (bit-identical output to sequential)
- Everything is seeded and deterministic

```bash
TerraGen.exe --db terrain.db --tectonic-plates 12 --rivers --spim --scan
```

**WorldGen** (settlements, roads, locations):
- Reads a TerraGen `terrain.db` and refuses to run without one
- Places settlements and a road network that respects terrain (rivers, coastline, slope) using the
  river graph TerraGen already computed
- Applies a settlement/road placement grammar and writes `LocationDescriptor` rows GET's
  `DefaultLocationService` can load directly
- Writes results to `world.db` (also SQLite, via the same `SqliteWorldDatabase` GET uses at runtime)

```bash
WorldGen.exe --terrain-db terrain.db --world-db world.db
```

Both tools force invariant culture for numeric I/O, so `--scan`'s printed `--lat-range`/`--lon-range`
hints always paste back into a follow-up command regardless of OS locale.

---

## WorldObserver: live realtime dashboard

An ASP.NET Core + SignalR web app that boots a `GameEngineTools` world (via `GameEngineToolsRuntime`)
and streams it live to a browser: character cards, a relationship graph, a map view over
TerraGen/WorldGen terrain and roads, per-character detail panels, and playback controls (pause, delay,
world tempo). Intended for watching a simulation run and clicking into individual characters, as
opposed to GameSandbox's text-diary output or LogsResolver's after-the-fact log analysis.

---

## LogsResolver: log analysis

A WPF desktop app that reads JSONL simulation logs and renders them for inspection — a native,
after-the-fact alternative to watching a simulation live in WorldObserver.

---

## TerrainEditor

A WPF desktop viewer for inspecting TerraGen/WorldGen output directly — heightmaps, rivers, roads, and
placed locations — without going through WorldObserver or a full simulation run.

---

## RelationshipsGame

An early-stage WPF prototype exploring UI built directly on the relationships/attraction engines.
Exploratory, pre-production.

---

## Building & Testing

> **`dotnet build` / `dotnet test` are broken here** — the .NET SDK 10.0.202 install is missing
> `Microsoft.Common.CurrentVersion.targets`. Use VS18 MSBuild + vstest instead.

```bash
# Build (EngineTests references the core lib, so this compiles both — fastest path)
"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" \
  "EngineTests/EngineTests.csproj" -t:Build -p:Configuration=Debug -verbosity:quiet

# Run all tests
"/c/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/Extensions/TestPlatform/vstest.console.exe" \
  "EngineTests/bin/Debug/net8.0/EngineTests.dll" --logger:"console;verbosity=minimal"

# Run a single test / one class
... vstest.console.exe ... --TestCaseFilter:"TestMethodName"
... vstest.console.exe ... --TestCaseFilter:"FullyQualifiedName~ClassName"
```

Tests use MSTest. `TestBase` provides DI setup, a `GameEngineToolsManager`, and deterministic test
doubles (`ZeroRandom`, `NullEventBus`, `NullScheduler`, `TestClock`, `FixedSocialFidelityPolicy`) and
calls `WWorld.Reset()` for isolation. `TerraGenTests`, `WorldGenTests`, `LogsResolverTests`, and
`TerrainEditorTests` are the matching suites for their respective tools.

---

## Project Layout

```
GameEngineTools/                 ← Core NPC simulation library (.NET 8)
  Characters/
    Core/                        ← IEngine, OrchestratedHuman, HumanContext, EnginesSnapshot, action slots
    Engines/
      Physiology/ Psychology/    ← body + affect
      Behavior/                  ← needs, modifiers, intent, arbitration, sleep, habits
      Interactions/ Objects/     ← social acts + object interaction
      Dialogue/ Language/        ← speech-act realization + per-character vocabulary
      Relationships/             ← directed social graph, investment, transgression
      Memory/ SemanticMemory/    ← episodic + person-belief memory
      Goals/ Values/ SelfConcept/ Interests/   ← long-term motivation & identity
      Schedule/                  ← daily routine + occupations
      Bereavement/               ← grief + widowhood hazard
      Status/ Economy/ Social/   ← social standing, money/wages, social comparison
      ToM/ Reputation/ LifeStage/ Attraction/  ← supporting social math
    Traits/                      ← Personality, Attachment, Values, Interests, sexual traits, appearance
    Generation/                  ← blueprint/appearance/personality/family generation, Portraits/
    Hosting/                     ← DI registration, LOD runtime, fidelity policies, occupation registry
    Persistence/                 ← EnginesSnapshot (de)serialisation
  World/
    Core/ (Time, Astro, Calendars)   ← WDateTime, sun model, calendars
    Location/ Movement/ Objects/ Data/ Simulation/
  Universe/                      ← Kepler orbital mechanics, star/moon/ring/habitability
  Narrative/                     ← Czech narrative formatter
  GameEngineToolsRuntime.cs      ← one-call bootstrap (DI + clock + engines + generation)
  GameEngineToolsManager.cs      ← character roster + generation helpers

EngineTests/         ← MSTest suite for GameEngineTools
GameSandbox/         ← Console simulation runner (canonical fully-wired example)
CharacterGenerator/  ← Interactive character-creation CLI
TerraGen/            ← Planet terrain generator (noise, tectonics, erosion, rivers, climate) → terrain.db
TerraGenTests/       ← MSTest suite for TerraGen
WorldGen/            ← Settlement/road/location generator on top of terrain.db → world.db
WorldGenTests/       ← MSTest suite for WorldGen
WorldObserver/       ← ASP.NET Core + SignalR realtime browser dashboard
LogsResolver/        ← WPF JSONL log viewer
LogsResolverTests/   ← Test suite for LogsResolver
TerrainEditor/       ← WPF viewer for TerraGen/WorldGen output
TerrainEditorTests/  ← Test suite for TerrainEditor
RelationshipsGame/   ← WPF prototype UI on the relationships engines
```
