# GameEngineTools (GET)

> **White-box autonomous NPC behavior simulation engine — C# / .NET 8**
> © 50PSoftware

![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

GET is a research-grade simulation platform that exposes the **full internal state** of characters
for study and iteration. It is not a presentation layer — it is a scientific sandbox for modeling
human physiology, psychology, memory, relationships, values, identity, and social behavior. Every
NPC perceives the world as **structured semantic data**, never as pixels, and runs a fixed
multi-engine pipeline every simulation tick.

---

## What You Can Do With It

- **Simulate a living social world.** Run dozens of autonomous characters that eat, sleep, work to a
  daily schedule, move between locations, form and decay relationships, gossip, fall in love, have
  children, age, and die — with no scripted behavior trees.
- **Inspect everything.** Every character exposes an immutable `EnginesSnapshot` with the live state
  of all 13 engines (PAD affect, stress/cortisol, needs, goals, beliefs about other people, values,
  self-esteem, …). Nothing is hidden behind a black box.
- **Generate believable people.** Deterministic, seedable generation of Big Five personality,
  appearance/genetics, attraction preferences, values, interests, and whole nuclear families with
  genetically-inherited children.
- **Drive psychology from the body and the world.** Pain, hunger, fever, sleep debt, ambient
  temperature, noise, crowding, privacy, daylight, and seasons all feed affect and decision-making.
- **Model real planetary mechanics.** Optional Kepler orbital stack drives day length, seasons,
  irradiance, ambient temperature, and gravity from a configurable star/planet/moon/ring system.
- **Persist and resume.** Characters serialize to/from JSON snapshots; world objects, the map, and
  social norms persist in SQLite.
- **Get narrative output.** A Czech-language narrative formatter turns domain events into a readable
  diary, with full morphological declension/conjugation.
- **Scale with LOD.** Per-character cognitive-resolution tiers (Player / Nearby / Background) control
  how often each character reasons and at what fidelity — so background crowds stay cheap.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Usage Recipes](#usage-recipes)
   - [Run a scene](#run-a-scene)
   - [Generate characters & families](#generate-characters--families)
   - [Read a character's state](#read-a-characters-state)
   - [Drive a character with events](#drive-a-character-with-events)
   - [Schedules & occupations](#schedules--occupations)
   - [World, locations & objects](#world-locations--objects)
   - [Astronomy & seasons](#astronomy--seasons)
   - [Persistence](#persistence)
   - [Level of detail (LOD)](#level-of-detail-lod)
3. [Architecture](#architecture)
   - [The engine pipeline](#the-engine-pipeline)
   - [Engine reference](#engine-reference)
   - [Supporting social systems](#supporting-social-systems)
   - [Traits](#traits)
4. [Configuration](#configuration)
5. [DI Registration](#di-registration)
6. [Building & Testing](#building--testing)
7. [Project Layout](#project-layout)

---

## Quick Start

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

---

## Usage Recipes

### Run a scene

`SimulationScene` owns the clock, ticks every character through the full pipeline in list order,
routes interaction outcomes between characters, injects ambient/celestial context, runs the
narrative formatter, and applies LOD. A richer setup:

```csharp
var opts = new SimulationSceneOptions
{
    Characters     = roster,                       // tick order = list order (player at index 0 by convention)
    LocationService = locationService,             // enables ContextChanged dispatch + InteractionSurface
    SimulationDays = 30,
    TickStep       = WTimeSpan.FromHours(0.5),
    InternalSubstep = WTimeSpan.FromMinutes(5),    // finer character-to-character latency
    AstroConfig    = astroConfig,                  // sun model → ambient temperature & daylight
    UniverseConfig = universeConfig,               // full Kepler planetary mechanics

    NarrativeFormatter = new DefaultNarrativeFormatter(),
    ResolveCharacter   = id => new NarrativeCharacterInfo(name, biology),
    OnNarrative        = entry => diary.Add(entry),

    DefaultCharacterLod = CognitiveResolutionLevel.Nearby,
    ResolveCharacterLod = ch => SceneCharacterLodResolver.Resolve(ch, playerId, locationService, hotSet),

    OnTick = (now, chars) => { /* scene logic: route ReachOut, handle ChildBorn, etc. */ },
};

await new SimulationScene(clock, opts, lodRuntime).RunAsync();
```

The per-step order is: apply LOD → compute celestial context → dispatch location changes → `OnTick`
callback → tick all characters → route outcomes → sleep prompts → advance clock → emit narrative.

### Generate characters & families

Generation is deterministic when seeded and produces a `HumanBlueprint` (+ immutable
`GeneticBlueprint`) that `DefaultHumanFactory` turns into a live `OrchestratedHuman`:

```csharp
// Single random person (uses the registered HumanBlueprintSpec).
IHuman person = manager.RandomizePerson();
IHuman young  = manager.RandomizePerson(maxAge: 25, sexBiology: SexBiology.Female, minAge: 18);

// A whole nuclear family with genetically-inherited children (requires AddFamilySystem()).
var familyGen = services.GetRequiredService<NuclearFamilyGenerator>();
var familyGraph = services.GetRequiredService<FamilyGraph>();
NuclearFamily family = familyGen.Generate(new NuclearFamilySpec(/* … */), familyGraph, clock.Now);
```

Generators are stadium-aware (`StadiumResolver` maps age → Baby / Child / Teenager / Adult / MidAged
/ Old). Children born during simulation (`ChildBorn`) are produced by `ChildBlueprintGenerator`
blending both parents.

### Read a character's state

Everything observable lives on the snapshot — read it directly, no reflection:

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

### Drive a character with events

Characters react to external stimuli delivered through the inbox (processed in Phase A of the next
tick), or immediately via `FlushInbox()` at setup time:

```csharp
person.ReceiveEvent(new ScheduleSlotTriggered(now, person.Id, slotId, ActionNames.SelfCare, "stables", 0.65));
person.SetHomeLocation("house_03");
person.ChangeOccupation("farmer");   // re-seeds the daily schedule
person.SetLastName(partner);         // e.g. on marriage
```

Outside a scene you can also tick a character manually: `person.Tick(now, dt)`.

### Schedules & occupations

A character's day is driven by an occupation looked up in `IOccupationRegistry` (built-ins plus
custom rows from `SourceFiles/Characters/Occupations.csv`). Each `ScheduleSlot` biases a preferred
action (and optionally a `MoveTo` toward a location) at a given hour, and can be skipped under stress.
Occupation schedules drive commuting (`MoveTo:*`) between home and workplace.

### World, locations & objects

```csharp
var locationService = new DefaultLocationService(socialNormProvider);
worldMap.RegisterAllLocations(locationService);        // bulk-register from CSV/SQLite
locationService.MoveCharacter(person.Id, "tavern_01"); // updates InteractionSurface next tick
```

`LocationDescriptor` carries noise, crowding, capacity, privacy, and type; the location service
computes a per-tick `InteractionSurface` (noise, crowding, privacy, proxemics) and dispatches
`ContextChanged` only to characters that moved. **World objects** (`WorldObject`) are perceived as a
category + a list of affordances; a character that uses one emits `ObjectAffordanceApplied`, which
Physiology/Psychology consume (e.g. a fireplace warms; a bench rests). Objects persist in SQLite and
can respawn on a schedule.

### Astronomy & seasons

Supply an `AstroConfig` (and optionally a `UniverseConfig`) to a scene and each tick gets a
`CelestialContext` — irradiance, day length, sunrise/sunset, season, and ambient temperature. With a
`UniverseConfig`, the `Universe/` Kepler stack (`KeplerSolver`, `OrbitalElements`, `StarPhysics`,
`MoonPhysics`, `RingSystem`, `HabitabilityProfile`) derives those from real orbital mechanics for a
configurable star/planet/moon/ring system.

### Persistence

```csharp
var gf = (GeneratedFile)services.GetRequiredService<IGeneratedFile>();
gf.Export(npc);                                  // write JSON snapshot (overloads: Export(PC) / Export(NPC))
NPC restored = gf.ImportNPC("npc_<guid>.jsonl"); // CharacterBase; restored.Person is the IHuman
// A character can also reload state in place:
restored.Person.RestoreSnapshot(snapshot, today); // revalidates age-dependent subsystems
```

`Characters/Persistence/` handles JSON (de)serialisation of `EnginesSnapshot`. Newer engine fields
are nullable for backward compatibility with older saves.

### Level of detail (LOD)

`CognitiveResolutionLevel` (Player / Nearby / Background) controls **decision cadence** (how often
Behavior reasons, via `IBehaviorCadencePolicy` + `Characters:Lod`) and **fidelity** of memory,
perception, and social processing (`Characters:Fidelity`). Resolve per character with
`ResolveCharacterLod`; background crowds reason hourly at reduced fidelity while the player reasons
every few minutes at full detail.

---

## Architecture

Each NPC is an **`OrchestratedHuman`**. Engines never call each other directly — they **read** the
shared per-tick `EnginesSnapshot` through `IHumanContext` and **emit** `IDomainEvent`s into an outbox
that the orchestrator drains and routes. Every engine implements the same contract
(`IEngine<TState, TConfig>`): `State`, `Config`, `Tick`, `Handle`, `RestoreState`.

### The engine pipeline

```
Phase A  ──  HandleScheduled + HandleInbox
             (scheduled actions + external events delivered against the PREVIOUS snapshot)

Phase B  ──  [LifeStage boundary check]
             Physiology → Psychology → [mid-tick snapshot refresh]
             → Behavior (cadence-gated) → Interactions → ObjectInteraction
             → Relationships → Memory → SemanticMemory
             → Goals → Schedule → Values → SelfConcept → Interests
             [final snapshot refresh]

Phase C  ──  SelfDeliver (≤ 8 passes)  →  snapshot refresh  →  PublishOutbox
             (character reacts to its own Phase-B events)
```

Invariants:

- **Order is load-bearing.** Physiology and Psychology run first; a **mid-tick snapshot refresh after
  Psychology** lets Behavior read the *current* tick's physio/psych state.
- **Behavior runs on a cadence** (LOD), while physiology/psychology/memory always advance with world
  time.
- **Death is terminal** — a dead character runs no engines but stays in the roster.
- **Action slots** (`ActiveActionSlots`) track occupied body/mind channels so Behavior can model
  multitasking instead of committing impossible action combinations.

### Engine reference

| Engine | State | What it owns |
|---|---|---|
| **Physiology** | `PhysiologyState` | Energy/hunger/thirst/pain/immune/temperature, sleep debt, allostatic load, cortisol, testosterone, nutrition, menstrual cycle, aging, injury, postpartum, mortality. Emits `ChildBorn`, `InjuryReceived`, death. |
| **Psychology** | `PsychologyState` | PAD affect, stress (HPA), cognitive load, cortisol, mood baseline, discrete emotions + decay, circadian arousal, hormonal/environmental/sickness modulation, stress manifestation. Anger is approach-motivated. |
| **Behavior** | `BehaviorState` | Decision core: 5 need engines + modifier engines (trait/affect/circadian/habit/memory/affordance/values/goal/schedule/investment) + intent stabilisation + action arbitration + habit learning. Emits `ActionCommitted`, `InteractionProposed`. |
| **Sleep** | `ISleepSession` | `Falling→Light→Deep→REM→Waking` state machine; nightmares, ambush, consolidation; outside the utility loop. |
| **Interactions** | `InteractionSurface` | Evaluates proposed social acts (8 `SpeechAct`s, 4 touch levels); misattribution under noise×stress; peak-end valence; sexual-encounter readiness gate; third-party observers. |
| **Object Interaction** | — | Applies world-object affordances; pickup/ownership routing. Optional engine. |
| **Relationships** | `RelationshipState` | Asymmetric directed graph: like/trust/closeness/respect/comfort/familiarity, attraction dimensions, communal vs exchange strength, Rusbult investment, transgression residue + repair, Navarro & Dunbar decay, attachment modulation. |
| **Memory** | `MemoryIndex` | Episodic encode/recall/forget: Ebbinghaus decay, spacing, peak-end salience, reconsolidation drift, stress distortion, System-1/2 switching; knowledge facts with confidence. |
| **Semantic Memory** | `SemanticMemoryState` | Per-person belief sets (Warm/EmotionallySafe/Reliable/Rejecting/Critical) distilled from episodes; attachment-modulated learning; feeds social targeting. |
| **Goals** | `GoalState` | Persistent long-term drives (existential/survival/career/relational) with salience/progress/frustration; bias utility, don't prescribe plans. |
| **Daily Schedule** | `DailyScheduleState` | Occupation-seeded time-of-day routine slots; biases action + movement; runtime occupation change. |
| **Values** | `ValuesState` | Drifting Schwartz `Current` vs immutable `Baseline`; congruence shifts utility & emits guilt on violation. |
| **Self-Concept** | `SelfConcept` | Perceived Big Five, ideal subset, self-esteem, self-discrepancy; evolves via self-verification; seeds `BuildIdentity` goals. |
| **Interests** | `InterestState` | Drifting RIASEC `Current` vs immutable `Baseline`; rewarded activity raises matching interest. |

### Supporting social systems

These are shared math/services, not pipeline engines:

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

---

## Configuration

Character config binds from `appsettings.Characters.json` under `Characters:*` via `IOptions<T>`;
`appsettings.Characters.Default.json` is the documented baseline (override per environment). World
and astronomy config bind from `appsettings.World.json` under `World:*`. Each config record lives
beside its engine.

Active `Characters:*` sections: `Physiology`, `MenstrualCycle`, `Psychology`, `Behavior`, `Sleep`,
`Interactions`, `Relationships`, `Memory`, `SemanticMemory`, `Goals`, `DailySchedule`, `Values`,
`SelfConcept`, `Interests`, `Lod` (decision cadence per LOD tier), `Fidelity` (memory/perception/
social fidelity per tier).

`World:*` sections: `Perception`, `Astro` (sun model, latitude, seasonal amplitude & thermal lag),
`Universe` (full star/planet/moon/ring definition), and `Calendar` (cultural overlay: month count,
target year length, time subdivisions, leap rule). The world clock/calendar is **derived from
physics** — `PlanetaryCalendarFactory` builds the `WorldTimeSpec` from `World:Universe` (planet
sidereal rotation → hours-per-day, orbit → year length) plus the `World:Calendar` overlay; there is
no separate `InitWorldClock` section. The default template is Earth; the sandbox ships an alternate
"Vigilia Insectianis" world (26-hour day, 10 months, 360-day year, +5 leap days every 4 years).

---

## DI Registration

`Characters/Hosting/ServiceCollectionExtensions.cs`. The shorthand registers all nine pipeline
engines at once; Values, SelfConcept, Interests, Goal, and support services come from
`AddCharactersCore`:

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
... vstest.console.exe ... --filter:"TestMethodName"
... vstest.console.exe ... --filter:"FullyQualifiedName~ClassName"
```

Tests use MSTest. `TestBase` provides DI setup, a `GameEngineToolsManager`, and deterministic test
doubles (`ZeroRandom`, `NullEventBus`, `NullScheduler`, `TestClock`, `FixedSocialFidelityPolicy`) and
calls `WWorld.Reset()` for isolation.

---

## Project Layout

```
GameEngineTools/                 ← Core library (.NET 8)
  Characters/
    Core/                        ← IEngine, OrchestratedHuman, HumanContext, EnginesSnapshot, action slots
    Engines/
      Physiology/ Psychology/    ← body + affect
      Behavior/                  ← needs, modifiers, intent, arbitration, sleep, habits
      Interactions/ Objects/     ← social acts + object interaction
      Relationships/             ← directed social graph, investment, transgression
      Memory/ SemanticMemory/    ← episodic + person-belief memory
      Goals/ Values/ SelfConcept/ Interests/   ← long-term motivation & identity
      Schedule/                  ← daily routine + occupations
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

EngineTests/        ← MSTest suite (build + run target)
GameSandbox/        ← Console simulation runner (canonical fully-wired example)
CharacterGenerator/ ← Interactive character-creation CLI
LogsResolver/       ← WPF JSONL log viewer (+ LogsResolverTests)
RelationshipsGame/  ← WPF prototype
```
