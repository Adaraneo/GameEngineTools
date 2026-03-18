# GameEngineTools

**A C# NPC simulation library for narrative-driven games.**  
Built by [50PSoftware](https://github.com/50PSoftware) · Unity-ready

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
  - [The Six-Engine Pipeline](#the-six-engine-pipeline)
  - [Event-Driven Communication](#event-driven-communication)
  - [Consumer Responsibility Pattern](#consumer-responsibility-pattern)
- [Core Systems](#core-systems)
  - [Physiology](#physiology)
  - [Psychology](#psychology)
  - [Behavior](#behavior)
  - [Interactions](#interactions)
  - [Relationships](#relationships)
  - [Memory](#memory)
- [World Time System](#world-time-system)
- [Character Generation](#character-generation)
- [Getting Started](#getting-started)
- [Logging](#logging)
- [Configuration Reference](#configuration-reference)
- [Project Structure](#project-structure)
- [Testing](#testing)
- [Dependencies](#dependencies)

---

## Overview

GameEngineTools is a character simulation library that models human behaviour through a pipeline of six independent but interconnected engines. Each character runs a deterministic simulation tick-by-tick, producing domain events consumed by the next engine in the chain. The result is emergent, psychologically grounded NPC behaviour — without scripted state machines.

The library is engine-agnostic and integrates with **Unity** via a VContainer adapter, or runs standalone in any `.NET 8` host (console, server, test harness).

---

## Architecture

### The Six-Engine Pipeline

Every character (`OrchestratedHuman`) runs engines in strict order each tick:

```
Physiology → Psychology → Behavior → Interactions → Relationships → Memory
```

| # | Engine | Responsibility |
|---|--------|----------------|
| 1 | **PhysiologyEngine** | Energy, hunger, thirst, pain, immune load, menstrual cycle |
| 2 | **PsychologyEngine** | Valence, arousal, dominance, stress, cognitive load, discrete emotion |
| 3 | **BehaviorEngine** | Utility-based action selection (need-weighted candidates) |
| 4 | **InteractionEngine** | Speech acts, touch attempts, misattribution, outcome resolution |
| 5 | **RelationshipsEngine** | Multi-dimensional relationship graph with decay and domain breakdown |
| 6 | **MemoryEngine** | Ebbinghaus forgetting curve, sleep consolidation, reinforcement (spacing effect) |

Each engine implements `IEngine<TState, TConfig>` — state is immutable records; config is injected via `IOptions<T>`.

### Event-Driven Communication

Engines communicate exclusively through **domain events** (`IDomainEvent`). An engine emits events into a per-tick `IEventCollector` (outbox); the orchestrator publishes them after all engines have ticked. Events are never shared mid-tick — this prevents ordering bugs and makes the simulation deterministic.

```csharp
// Engine emits an event:
outbox.Add(new InteractionOutcomeDecided(now, initiatorId, targetId, outcome));

// Another engine reacts to it in the next tick via Handle():
public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
{
    if (@event is InteractionOutcomeDecided e) { /* ... */ }
}
```

### Consumer Responsibility Pattern

The library exposes events; **wiring them into game logic is the consumer's responsibility**. This is a deliberate architectural boundary. For example, `BehaviorEngine` emits `InteractionProposed` — the game decides who the target is and routes the event to the appropriate character.

---

## Core Systems

### Physiology

Tracks six continuous physiological values on `[0, 100]` scales:

- `Energy` — depleted by activity, restored by sleep
- `Hunger` / `Thirst` — increase over time, trigger eating/drinking behaviour
- `Pain` — passive recovery per hour; sleep accelerates recovery
- `ImmuneLoad` — gradual accumulation, affects psychology
- `BodyTempDelta` — deviation from baseline temperature

Optional: **menstrual cycle simulation** (configurable age of onset, cycle length distribution, PMS probability, ovulation window events).

### Psychology

Models affect using a three-dimensional PAD (Pleasure-Arousal-Dominance) space:

- `Valence` — current hedonic state (−1..+1)
- `Arousal` — activation level
- `Dominance` — sense of control
- `Stress` — cumulative, decays at a configurable rate per hour
- `CognitiveLoad` — mental fatigue
- `DominantEmotion` — discrete categorical label (`Neutral`, `Happy`, `Sad`, `Angry`, `Afraid`, `Disgusted`, `Surprised`)

Neuroticism (BigFive trait) modulates stress sensitivity.

### Behavior

Utility-based action selection. Candidate actions are scored by:

```
Utility(need, weight) = need * (0.5 + personalityWeight)
```

Needs are computed fresh from the current `EnginesSnapshot` every tick — `BehaviorState` is only for persistence, not for driving decisions.

**Inertia and novelty penalties** prevent constant action switching. **Sleep sub-system** runs as a state machine within the engine:

- Detects fatigue (`SleepDebt`, `Energy`) and emits `SleepPromptRequested` for player confirmation
- Handles player `SleepConfirmed` / `SleepDeclined` 
- Progresses through phases: Falling → Light → Deep → REM
- Nightmares (stress-dependent), ambush events, and interrupted sleep are all modelled

### Interactions

Handles social actions between characters:

- **Speech acts**: `SmallTalk`, `Question`, `SelfDisclosure`, `Validation`, `Humor`, `Meta`, `Invite`, `Boundary`
- **Physical touch**: graded intensity levels; acceptance probability based on relationship state
- **Misattribution**: configurable base rate — a character may attribute an outcome to the wrong cause

Outcomes are resolved asymmetrically: rejection affects initiator and recipient differently.

### Relationships

A directed, weighted graph of `RelationshipEdge` records. Each edge carries six dimensions:

| Dimension | Neutral | Notes |
|-----------|---------|-------|
| `Like` | 50 | Affected by all positive/negative interactions |
| `Trust` | 50 | Slow decay; hard to rebuild |
| `Closeness` | 35 | Fastest decay; proximity-dependent |
| `Attraction` | 35–45 | Anchors at different baselines based on initial value |
| `Respect` | 55 | Very slow decay |
| `Comfort` | 45 | Modulated by valence and stress |

**Domain breakdown** (`DomainBreakdown`) tracks which facets of the relationship have developed (Humor, Intellect, Values, Physical). Each speech act type contributes to specific domains.

Decay is modulated by the character's current psychology: positive valence slows decay; stress accelerates it.

### Memory

Implements three cognitively-grounded principles:

1. **Ebbinghaus forgetting curve** — exponential decay: `strength *= exp(-k * emotionMod * Δt)`
   - Negative memories decay *slower* (lower `emotionMod`) — trauma persists
   - Positive memories fade faster than neutral ones

2. **Sleep consolidation** — on `SleepEnded`, the top 10 episodes by salience are strengthened by `SleepConsolidationBoost`

3. **Spacing effect** — a repeated experience of the same type reinforces the existing episode rather than creating a duplicate

**Memory → Behavior integration**: `ApplyMemoryModifiers()` in `BehaviorEngine` reads the memory index and adjusts action utilities:
- Social trauma reduces `ReachOut` utility
- Positive social memories boost social inclination
- Intimate rejection penalises `InviteIntimacy`
- High emotional load increases `SelfCare`

---

## World Time System

GameEngineTools uses a fully configurable fictional calendar, decoupled from real-world time.

### Key Types

| Type | Purpose |
|------|---------|
| `WDateTime` | A point in world time (stored as `long worldTicks` from epoch) |
| `WDateOnly` | Date without time |
| `WTimeOnly` | Time of day |
| `WTimeSpan` | Duration |
| `WorldTimeSpec` | Calendar definition (hours/day, days/month, leap rules) |
| `IWorldCalendar` | Calendar abstraction (`FixedMonthsCalendar` provided) |

`WDateTime` properties (`Year`, `Month`, `Day`, `Hour`...) work as ambient accessors — no context passing required, analogous to `System.DateTime`. Pure arithmetic operations (operators, comparison) do **not** require `WWorld` to be configured.

### Configuration (`appsettings.json`)

```json
"InitWorldClock": {
  "UseWorldType": "MyWorld",
  "MyWorld": {
    "TicksPerSecond": 1,
    "SecondsPerMinute": 60,
    "MinutesPerHour": 60,
    "HoursPerDay": 26,
    "DaysInMonths": [36, 36, 36, 36, 36, 36, 36, 36, 36, 36],
    "LeapYearInterval": 5,
    "LeapExtraDays": 1
  }
}
```

---

## Character Generation

Characters are generated from a `HumanBlueprint` via `IHumanBlueprintGenerator`.

### Personality Model

```
BigFive (O, C, E, A, N)
  └─ correlated generation via Cholesky decomposition
  └─ mapped to MotivationWeights (9 drives)
       Affiliation · Achievement · Power · Altruism · Competence
       Autonomy · Curiosity · Rest · Sexuality
  └─ drives directly scale Behavior utility weights

AttachmentStyle   — Secure / Anxious / Avoidant / Disorganized
CommunicationStyle — Direct / Indirect / HighContext / LowContext
Sociosexuality    — Restricted / Intermediate / Unrestricted
Chronotype        — Lark / Neutral / Owl
```

Generation is **deterministic with a seed** — the same seed always produces the same character.

### Usage

```csharp
var blueprint = generator.Generate(new HumanBlueprintRequest
{
    Sex = SexBiology.Female,
    PersonalityHints = new PersonalityHints(Extraversion: 0.8, Neuroticism: 0.3),
    Seed = 42
});

var character = manager.AddCharacter(blueprint);
```

---

## Getting Started

### 1. Configure `appsettings.json`

Add `InitWorldClock` and `Characters` sections (see `appsettings.Characters.json` for all defaults).

### 2. Start the Runtime

```csharp
await using var runtime = await GameEngineToolsRuntime.StartAsync(
    consoleLogs: true,
    logsRoot: "logs"
);

var manager = runtime.Services.GetRequiredService<IGameEngineToolsManager>();
await manager.InitializeAsync();
```

### 3. Run a Simulation

```csharp
var scene = new SimulationScene(clock, new SimulationSceneOptions
{
    Characters = manager.Characters,
    TickStep   = WTimeSpan.FromHours(1),
    SimulationYears = 2,

    OnTick = (now, characters) =>
    {
        // ReachOut routing — consumer decides who interacts with whom
        var actor  = characters[0];
        var target = characters[1];

        if (actor.LastOutbox.OfType<InteractionProposed>().Any())
        {
            target.ReceiveEvent(new InteractionProposed(now, actor.Id, target.Id, SpeechAct.SmallTalk));
        }
    },

    SleepPromptHandlers = new Dictionary<HumanId, Func<SleepPromptRequested, bool>>
    {
        // Player character — ask the player
        [playerId] = prompt => AskPlayer(prompt)
        // NPCs — auto-confirm (missing handler = auto-confirm)
    }
});

await scene.RunAsync();
```

### 4. Loading Saved State

```csharp
// Load WorldTimeSpec before starting the runtime (e.g. to parse saved ticks)
var spec  = GameEngineToolsRuntime.LoadSpec();
long ticks = savedTicksString != null ? long.Parse(savedTicksString)
           : spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;

await using var runtime = await GameEngineToolsRuntime.StartAsync(new WDateTime(ticks));
```

---

## Logging

Logging uses `[LoggerMessage]` source generation — **zero allocations** at runtime, compile-time validation.

All log methods live in `CoreLog` (the single source of truth for EventIds). Never call raw `_log.LogXxx()` inside engines — always use `CoreLog` extension methods.

### EventId Ranges

| Range | Domain |
|-------|--------|
| 1000–1099 | Behavior — decisions, actions, cooldowns |
| 1100–1199 | Sleep — phases, prompts, nightmares, ambush |
| 1200–1299 | Interactions — outcomes, touch |
| 2000–2999 | Relationships — edges, decay, stages |
| 3000–3999 | Memory — encoding, consolidation |
| 4000–4999 | Infrastructure — Scheduler |
| 5000–5999 | Snapshots — Physiology / Psychology / Behavior |

### Per-Character Log Files

Each character gets its own log file per engine under `logs/Characters/Person/{guid}/{Engine}.log`.  
Routing is done via `CharacterLogScope` — no string parsing, direct type check only.

```csharp
using (_log.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(DefaultMemoryEngine))))
{
    _log.MemoryEncoded(ctx.Id.Value.ToString(), tag, salience, emotion.ToString());
}
```

---

## Configuration Reference

All values live under `Characters:` in `appsettings.json`.

### Physiology

| Key | Default | Description |
|-----|---------|-------------|
| `RestingMetabolicRate` | 1600 | Calories burned at rest (future use) |
| `MaxSleepDebtHours` | 12 | Cap on accumulated sleep debt |
| `EnableMenstrualCycle` | true | Enable hormonal cycle simulation |
| `MenstrualCycleBeginsInAge` | 12 | Minimum age for cycle activation |
| `EnergyRecoveryPerSleepHour` | 10 | Energy restored per hour of sleep |
| `PainPassiveRecoveryPerHour` | 0.3 | Passive pain reduction per hour |
| `PainSleepRecoveryPerHour` | 0.5 | Additional pain reduction during sleep |

### Psychology

| Key | Default | Description |
|-----|---------|-------------|
| `BaselineAffectVariance` | 0.02 | Random drift in valence/arousal each tick |
| `StressRecoveryRatePerHour` | 1.5 | Stress reduction per hour |
| `SleepQualityAffectWeight` | 0.5 | How much sleep quality shifts valence |

### Behavior

| Key | Default | Description |
|-----|---------|-------------|
| `InertiaWeight` | 0.25 | Bonus for continuing the current action |
| `NoveltyPenalty` | 0.1 | Penalty for switching to a new action |
| `PlanningHorizonHours` | 2.0 | Look-ahead for need projection |
| `BaseSleepHours` | 8 | Target sleep duration |
| `SleepCooldownHours` | 16 | Minimum time between sleep sessions |

### Memory

| Key | Default | Description |
|-----|---------|-------------|
| `BaseEncoding` | 0.5 | Initial strength of a new episodic memory |
| `ForgettingRate` | 0.06 | Decay rate constant `k` in Ebbinghaus formula |
| `EmotionDecayMod` | 0.5 | Multiplier for negative emotion decay (lower = slower) |
| `SleepConsolidationBoost` | 0.12 | Strength added to top episodes after sleep |
| `ReinforcementBoost` | 0.15 | Strength added when an existing episode is reinforced |
| `PruneThreshold` | 0.01 | Episodes below this strength are removed |

### Relationships

| Key | Default | Description |
|-----|---------|-------------|
| `DecayPerDay` | 1.5 | Base decay applied to all dimensions per day |
| `RepairGain` | 6.0 | `Like` gain from a repair interaction |
| `RupturePenalty` | 8.0 | `Like` penalty from a rupture interaction |

---

## Project Structure

```
GameEngineTools/
├── Characters/
│   ├── Core/               # IHuman, IEngine, OrchestratedHuman, domain contracts
│   ├── Engines/
│   │   ├── Physiology/
│   │   ├── Psychology/
│   │   ├── Behavior/
│   │   ├── Interactions/
│   │   ├── Relationships/
│   │   └── Memory/
│   ├── Generation/         # HumanBlueprintGenerator, PersonalityGenerator
│   ├── Hosting/            # DI registration (AddCharacters, AddCharacterGeneration)
│   └── Traits/             # Personality, BigFive, MotivationWeights
├── World/
│   ├── Core/               # WorldTimeSpec, IWorldCalendar, FixedMonthsCalendar
│   ├── Utils/Time/         # WDateTime, WDateOnly, WTimeOnly, WTimeSpan
│   └── Simulation/         # SimulationScene
├── Logging/                # CoreLog, CharactersFileLoggerProvider, CharacterLogScope
├── Config/                 # All *Config records
├── GameEngineToolsRuntime.cs   # DI entry point
└── GameEngineToolsManager.cs   # Character management, factory methods

EngineTests/                # MSTest integration tests
GameSandbox/                # Console host for development & diary output
```

---

## Testing

Integration tests use `MSTest` and build a real DI container with in-memory overrides. The base class `TestBase` wires up all six engines with default configuration.

All behavior utility tables are documented analytically in test comments — exact utility values per scenario are shown to make calibration decisions traceable.

```
dotnet test EngineTests/
```

---

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.DependencyInjection` | DI container |
| `Microsoft.Extensions.Logging` | Structured logging with source generation |
| `Microsoft.Extensions.Options` | Typed configuration |
| `Microsoft.Extensions.Configuration.Json` | `appsettings.json` loading |

Unity integration uses VContainer with a custom `IServiceCollection` adapter — all `ServiceCollectionExtensions` registration methods work unchanged.

---

## License

Copyright © 50PSoftware. All rights reserved.
