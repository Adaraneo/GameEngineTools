# GameEngineTools (GET)

> **White-box autonomous NPC behavior simulation engine — C# / .NET 8**
> © 50PSoftware

![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

GET is a research-grade simulation platform that exposes the full internal state of characters
for study and iteration. Unlike commercial titles, GET is not a presentation layer — it is a
scientific sandbox for modeling human physiology, psychology, memory, relationships, values,
identity, and social behavior. Every NPC perceives the world as **structured semantic data**,
never as pixels.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [The Engine Pipeline](#the-engine-pipeline)
3. [Engines](#engines)
   - [Physiology](#physiology)
   - [Psychology](#psychology)
   - [Behavior](#behavior)
   - [Sleep](#sleep)
   - [Interactions](#interactions)
   - [Object Interaction](#object-interaction)
   - [Relationships](#relationships)
   - [Memory](#memory)
   - [Semantic Memory](#semantic-memory)
   - [Goals](#goals)
   - [Daily Schedule & Occupations](#daily-schedule--occupations)
   - [Values](#values)
   - [Self-Concept](#self-concept)
   - [Interests](#interests)
4. [Supporting Social Systems](#supporting-social-systems)
   - [Theory of Mind](#theory-of-mind)
   - [Community Reputation](#community-reputation)
   - [Life-Stage Transitions](#life-stage-transitions)
   - [Attraction](#attraction)
5. [Traits](#traits)
6. [Character & Family Generation](#character--family-generation)
7. [World, Objects & Astronomy](#world-objects--astronomy)
8. [SimulationScene](#simulationscene)
9. [Configuration](#configuration)
10. [DI Registration](#di-registration)
11. [Building & Testing](#building--testing)
12. [Project Layout](#project-layout)

---

## Architecture Overview

GET models each NPC as an **`OrchestratedHuman`** — a character that runs a fixed multi-engine
pipeline on every simulation tick. All internal state is exposed via an immutable
**`EnginesSnapshot`**, making the system suitable for debugging, visualization, persistence, and
research.

Every engine implements the same contract (`IEngine<TState, TConfig>` in
`Characters/Core/Core.cs`):

```csharp
TState State { get; }
TConfig Config { get; }
void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox);
void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox);
void RestoreState(TState state);
```

Engines never call each other directly. They communicate by:

- **Reading** the shared per-tick `EnginesSnapshot` through `IHumanContext`, and
- **Emitting** `IDomainEvent`s into an outbox that the orchestrator drains and routes.

Selected scientific frameworks used internally:

| Framework | Used for |
|---|---|
| PAD emotional model | Valence / Arousal / Dominance affect state |
| Big Five (OCEAN) | Personality generation + pervasive behavioral modulation |
| HPA-axis / allostatic load | Stress, cortisol, cumulative physiological burden |
| Ebbinghaus decay + peak-end rule | Episodic memory forgetting & salience |
| 2D Anxiety × Avoidance (ECR-R) | Continuous attachment, not categorical |
| Schwartz Basic Human Values | Moral value loadings + drift |
| Holland RIASEC | Vocational interests + drift |
| Higgins self-discrepancy / Swann self-verification | Self-concept evolution |
| Dual Control Model (SES/SIS) + SOI-R | Sexual responsiveness & sociosexuality |
| Kepler orbital mechanics | Day length, seasons, irradiance, ambient temperature |

---

## The Engine Pipeline

`OrchestratedHuman` processes each tick in **three phases**:

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

Key invariants:

- **The order is load-bearing.** Physiology and Psychology advance first because every downstream
  engine reads their state. A **mid-tick snapshot is refreshed after Psychology** so Behavior sees
  the *current* tick's physio/psych state — not last tick's.
- **Behavior runs on a cadence.** `IBehaviorCadencePolicy` decouples expensive behavioral reasoning
  from the base tick rate per LOD tier; physiology/psychology/memory always advance with world time.
- **Death is terminal.** A character whose `PhysiologyState.Status == Dead` runs no engines and
  emits no events, but stays in the roster for lookups.
- **Action slots.** `ActiveActionSlots` tracks which body/mind channels (Hands, Mind, Posture, …)
  are occupied by an in-flight action, so the behavior engine can gate concurrent/secondary actions
  (multitasking) rather than committing physically impossible combinations.

---

## Engines

### Physiology

**State:** `PhysiologyState` — Energy, Hunger, Thirst, Pain, ImmuneLoad, BodyTempDelta,
SleepDebtHours, `AllostaticLoad`, `CortisolLevel`, `Testosterone`, `Nutrition` (Calories, VitaminD,
Iron, Protein, BloodGlucose), `Cycle` (menstrual), `Aging` (grey fraction, wrinkles, hair density,
muscle/bone), `Status` (Alive/Dead), injury & postpartum state.

Models the body's physical condition: advances biological needs and recovery, runs the menstrual
cycle (phase transitions, ovulation window, PMS/PMDD symptoms), injury healing, postpartum recovery,
nutrition tracking, biological aging, and mortality. Emits reproductive events
(`PregnancyStarted`, `PregnancyDiscovered`, `ChildBorn`), `InjuryReceived`/`InjuryHealed`, and
death. Runs first because pain, hunger, fever, and sleep debt are upstream inputs to mood, stress,
and decision-making.

### Psychology

**State:** `PsychologyState` — PAD (`Valence` [−1..+1], `Arousal`/`Dominance` [0..1]), `Stress`,
`AllostaticLoad`, `CognitiveLoad`, `Cortisol`, `MoodBaseline`, `DominantEmotion`, `Motivations`.

Computes the continuous PAD affective state plus derived scalars each tick: stress recovery and
HPA-axis growth (Neuroticism-modulated), PAD drift toward resting baseline (positive valence
baseline), physiology modulation (pain → stress, sleep debt → cognitive load, fever → arousal
suppression), circadian arousal rhythm, hormonal coupling (cortisol, testosterone), environmental
effects (noise, crowding, temperature, proxemics, privacy, isolation), sickness behavior (anhedonia,
lethargy, brain fog), discrete-emotion inference and per-emotion decay, and stress manifestation.
**Anger is approach-motivated** (raises confrontational utility). Emits `MotivationChanged`,
`StressSpiked`, `StressManifested`. Value-congruence violations from the Values system land here as a
guilt spike.

### Behavior

The decision-making core. `DefaultBehaviorEngine` is a composition, not a monolith:

- **5 need engines** (`IBehaviorNeedEngine`) — `PhysiologicalNeedsEngine`, `SocialNeedsEngine`,
  `CompetenceNeedsEngine`, `AutonomyExplorationNeedsEngine`, and `ContingencySearchEngine` (the
  foraging bridge: emits `MoveTo:Food`/`MoveTo:Drink` when a needed object is absent at the current
  location). Each produces scored `BehaviorCandidate`s.
- **Modifier engines** (`IBehaviorModifierEngine`) reshape candidate utilities — trait bias,
  affective state, circadian arousal, habit/routine, learned habit, memory influence, environmental
  & world-object affordance, psychological conflict, **values congruence**, **goal pressure**,
  **daily-schedule bias**, **relationship investment**, object-interaction bias, and the
  **object-affordance gate** (hard/soft presence gate run after scoring, before intent management).
- **`IIntentManagementEngine`** — stabilises direction across ticks with hysteresis and an emergency
  physiological override.
- **`IActionArbitrationEngine`** — selects the final action; resolves conflict via ambivalence and
  tension; supports multitasking via action slots.
- **Habit learning** (`BehaviorHabitLearning`) — strengthens, decays, and prunes habit traces;
  classifies them Adaptive / Neutral / MaladaptiveCoping.

Emits `ActionCommitted`, `InteractionProposed`, `SleepConfirmed`/`SleepPromptRequested`.

**Action names** (`Characters/Engines/ActionNames.cs`): `Work`, `Create`, `Eat`, `Drink`,
`SelfCare`, `ReachOut`, `InviteIntimacy`, `Flee`, `Fight`, `Idle`, `Sleep`; movement
`MoveTo:Social/Private/Work/Rest/Public/Food/Drink`; object interaction `InteractWithObject` and the
affordance family `UseObject:Rest/Work/Fun/Warmth/Mood/Social`.

### Sleep

`ISleepCoordinator` (owned by Behavior) drives an `ISleepSession` state machine:
`Falling → Light → Deep → REM → Waking`. It rolls nightmare probability from stress, ambush
probability for outdoor sleep (reduced by a companion guard), applies sleep-overdue decline
penalties, and fires `NightmareTriggered`/`DreamOccurred` in REM plus `SleepEnded` with a quality
score. Downstream: Physiology recovers Energy/ImmuneLoad/Pain, Psychology adjusts Stress/MoodBaseline,
Memory runs consolidation. Sleep is handled **outside** the utility-arbitration loop.

### Interactions

**State:** `InteractionSurface` — Location, HasPrivacy, Noise, Crowding, `SurfaceKind`,
`ProxemicDistanceMeters`, and `Observers` (third-party `HumanId`s).

Evaluates proposed social interactions and decides acceptance from the relationship edge,
psychological state, and environment. Speech acts (`SpeechAct`): `SmallTalk`, `Question`,
`SelfDisclosure`, `Validation`, `Boundary`, `Humor`, `Meta`, `Invite`. Touch levels: `None`,
`Light`, `Friendly`, `Intimate`. A **misattribution penalty** scales with noise × stress × the
emotional weight of the speech act. Computes peak-end valence for memory. When an accepted `Invite`
meets relationship-readiness thresholds, emits `SexualEncounterProposed`. Presence of `Observers`
triggers `ThirdPartyActionObserved` (reputation/witness effects). Emits `InteractionOutcome`,
`TouchOutcome`, `SexualEncounterOutcome`.

### Object Interaction

Optional engine (`IObjectInteractionEngine`), wired between Interactions and Relationships when
registered. NPCs perceive objects as structured `WorldObject` data (a category + a list of
`WorldObjectAffordance`). The `AffordanceApplicationService` applies a used object's affordances by
emitting `ObjectAffordanceApplied` events that Physiology/Psychology consume; `Ownership`
affordances are routed to the object-interaction engine (pickup/inventory) instead. See
[World, Objects & Astronomy](#world-objects--astronomy).

### Relationships

**State:** `RelationshipState` — an asymmetric directed graph of `RelationshipEdge` per `HumanId`.

Each `RelationshipEdge` tracks (all [0–100] unless noted):

- **Core:** `Like`, `Trust`, `Closeness`, `Respect`, `Comfort`, `Familiarity` (non-monotonic with
  Like — high familiarity without positive interaction drifts Like down).
- **Attraction:** `AestheticAttraction`, `PhysicalAttraction`, `RomanticInterest`, `SexualInterest`
  (fastest-decaying; Coolidge effect).
- **Relationship type:** `CommunalStrength` (need-responsive; tracking favors *hurts* a high-communal
  bond) and `ExchangeStrength` (equity-based; independent).
- **Investment model** (Rusbult): accumulated investment size raises commitment and dependence;
  dissolution emits an investment-loss event consumed by Psychology.
- **Repair:** `TransgressionResidue` (power-law decay; reduced by Lewicki-weighted apology
  components) and the terminal `IsContemptuouslyDestroyed` flag.
- **Desire:** `ResponsiveDesireLevel` (Basson 2001), grows with CommunalStrength + history.
- **Kinship:** `KinRole` (Partner, Parent, Child, …) and meta (`PositiveInteractionCount`,
  `LastContactTime`, `TargetBiology`).

Seeded via `IAttractionCalculator` on `FirstImpressionFormed`. Decay accelerates under the Navarro
8× contact-gap rule and Dunbar tier-capacity pressure. Attachment style modulates how strongly every
dimension updates. Third-party reputation effects (`ThirdPartyActionObserved`, Feinberg 2014) update
an observer's edge at a fraction of direct-interaction weight.

### Memory

**State:** `MemoryIndex` — episodic memories plus a `Knowledge` store of `SemanticFact`s.

Episodic encoding, retrieval, consolidation, and forgetting: **Ebbinghaus decay**, **spacing effect**
(repeats reinforce the existing episode via a reinforcement key rather than duplicating),
**peak-end rule** salience, **reconsolidation** drift on each recall (negative episodes drift
faster), and **stress distortion** at encoding. `MemoryCognition.BuildWorkingSet()` implements
System 1 / System 2 switching: above a cognitive-burden threshold (shifted by Conscientiousness),
episodic recall is skipped in favour of semantic reflection summaries. Sleep (`SleepEnded`) triggers
consolidation. Knowledge facts carry confidence (direct witness vs. gossip) that decays over time.

### Semantic Memory

**State:** `SemanticMemoryState` — a `PersonBeliefSet` per `HumanId`.

Distills episodic patterns into per-person beliefs (`PersonBeliefKind`: `Rejecting`,
`EmotionallySafe`, `Reliable`, `Warm`, `Critical`), each with `Strength`, `Stability`, and
`EvidenceCount`. Confirming evidence strengthens and stabilises; contradiction weakens (high-stability
beliefs resist). Attachment style modulates learning rate (Anxious ≈ 1.30×, Avoidant ≈ 0.75×). A
Navarro toxic-ratio rule accelerates decay (rapid disillusionment). Feeds social targeting
(`SemanticTargeting`, `ExpectedAcceptance`) used by Behavior to choose `ReachOut`/`InviteIntimacy`
targets — and acts as the semantic fallback for unfamiliar people.

### Goals

**State:** `GoalState` — a list of `PersistentGoal`.

Long-term motivational drives that bias utility-based action selection without prescribing a plan.
Each goal has `Salience` (motivational pressure), `Progress`, and `Frustration`. `PersistentGoalKind`
covers existential (FindMeaning, OvercomeTrauma, BuildIdentity), survival (ProtectFamily,
EscapeDanger), career (MasterCraft, BuildReputation), and relational (FindPartner, RepairRelationship,
SeekRevenge) drives. Goals are seeded from personality, triggered by events, or scripted
(`GoalInjected`). `GoalBehaviorModifier` translates salience into per-action utility pressure. Emits
`GoalActivated`, `GoalProgressed`, `GoalResolved` (Completed / Abandoned / Faded / Displaced).

### Daily Schedule & Occupations

**State:** `DailyScheduleState` — a list of time-anchored `ScheduleSlot`s + the active occupation.

Anchors a character's routine to the world `IScheduler`: each slot fires at an hour of day, biases a
preferred action (and optionally a `MoveTo` toward a location) via `DailyScheduleBehaviorModifier`,
and can be skipped under high stress / low energy. Slots are seeded from an occupation looked up in
`IOccupationRegistry` (built-in occupations via `BuiltInOccupationRegistrar`, plus custom ones from
`Occupations.csv`) and modulated by personality/chronotype. `IHuman.ChangeOccupation()` re-seeds the
schedule at runtime. Emits `ScheduleDayRegistered`, `ScheduleSlotTriggered`, `ScheduleSlotBiasApplied`.

### Values

**State:** `ValuesState` — a drifting `Current` Schwartz `ValuesProfile` plus an immutable
`Baseline`.

"Prior, not constant": the baseline is seeded once from Big Five; `Current` starts equal, drifts from
value-congruent/value-violating action, and slowly regresses toward baseline (Vecchione 2016
rank-order stability). `ValuesBehaviorModifier` shifts action utility by value congruence and emits
`ValueCongruenceViolated` (→ guilt spike in Psychology). Morality is keyed to who the character
*became*, not who they were born to be.

### Self-Concept

**State:** `SelfConcept` — perceived Big Five, an ideal-self subset, global `SelfEsteem`, and
`SelfDiscrepancy`.

A character's self-view evolves from social feedback via Swann self-verification (confirming feedback
is accepted; disconfirming feedback discounted). `SelfDiscrepancy` (Higgins, used only as a general
discrepancy→distress signal) drives identity work: crossing a threshold seeds a `BuildIdentity` goal.
Emits `MetaperceptionUpdated`.

### Interests

**State:** `InterestState` — a drifting `Current` RIASEC `InterestProfile` plus an immutable
`Baseline`.

Shares the "prior, not constant" pattern with Values: rewarding experience raises a matching interest,
and regression toward baseline is the brake on the interest → salience → interest feedback loop.

---

## Supporting Social Systems

These are not pipeline engines but shared math/services consumed by the engines above.

### Theory of Mind

`ToMMath` (`Characters/Engines/ToM/`) models recursive belief reasoning ("I think that she thinks
that I…"). Each NPC has a generated recursion ceiling (population mean ≈ 4, SD ≈ 1) with a default
working depth of 2, used by interaction and deception reasoning. `MutualKnowledgeFormed` captures the
emergence of common knowledge between characters.

### Community Reputation

A **scene-level singleton** `CommunityReputationLedger` (`Characters/Engines/Reputation/`) folds
observed acts about a subject at a locale into an aggregate reputation, with recency weighting
(half-life ≈ 7 interactions), stern-judging negativity bias (bad acts move ≈ 1.5× as hard), and
diffusion through the community. `ReputationMath` exposes a trust prior derived from a subject's
spread reputation — the prior a stranger starts from.

### Life-Stage Transitions

`OrchestratedHuman` detects life-stage boundary crossings each tick and emits
`LifeStageTransitionOccurred`. `LifeStageMath` provides probabilistic reappraisal hooks (e.g. midlife
mood dip, parenting-identity shifts) — no scripted crisis, just a believability trigger for
re-evaluation.

### Attraction

`DefaultAttractionCalculator` (`Characters/Engines/Attraction/`) is a pure, stateless, asymmetric
function. Components: `BasePhysical` (WHR + height + symmetry), `PreferenceMatch` (the observer's
height/frame/WHR/symmetry/age preferences), `StateModifier` (posture, acne, bloating), a mere-exposure
familiarity bonus, and the Zillmann excitatory-transfer bonus. Sexual-orientation weight multiplies
the physical components. Called per-pair on demand (e.g. at first impression).

---

## Traits

`Characters/Traits/` holds the stable, slow-changing trait layer:

- **`Personality`** — Big Five (OCEAN), each [0–1]; pervasive across every engine.
- **`AttachmentProfile`** — continuous 2-D ECR-R model: `Anxiety` × `Avoidance` (Secure /
  Preoccupied / Dismissing / Fearful are region shortcuts, **not** an enum).
- **`ValuesProfile`** — Schwartz value loadings.
- **`InterestProfile`** — Holland RIASEC interests.
- **`PsychologicalProfile`** — composite trait bundle derived from personality.
- **`SexualResponsiveness`** — Dual Control Model (SES / SIS1 / SIS2); `DualControlMath` &
  `DualControlBehaviorMath`.
- **`SociosexualityBehaviorMath`** — SOI-R; **`SexualOrientation`** + behavior math.
- **`PhysicalAppearance`**, **`Morphology`**, **`AttractionProfile`**.

---

## Character & Family Generation

Before simulation, a character must be generated (`Characters/Generation/`). The pipeline is a
separate, deterministic-when-seeded subsystem that produces a `HumanBlueprint` (and an immutable
`GeneticBlueprint`); `DefaultHumanFactory` wraps it into a live `OrchestratedHuman`.

- **`HumanBlueprintGenerator`** — orchestrates the full blueprint; all generators are stadium-aware
  (`StadiumResolver` maps age → `StadiumType`: Baby / Child / Teenager / Adult / MidAged / Old).
- **`PersonalityGenerator`** — draws each Big Five trait from a `TraitDistribution` (inverse-normal
  CDF with skew correction) and applies population-level correlations via Cholesky decomposition
  (e.g. C↔N ≈ −0.35).
- **`AppearanceGenerator`** + **`AppearanceProjector`** — generate the immutable genetic blueprint and
  project the visible appearance at a given age (aging changes the projection, not the genes).
- **`AttractionProfileGenerator`** — personal physical preferences and sexual orientation.
- **`ChildBlueprintGenerator`** — blends two parents' traits into a newborn blueprint (used on
  `ChildBorn`).
- **Family system** (`AddFamilySystem`) — `FamilyGraph`, `FamilyBuilder`, and `NuclearFamilyGenerator`
  generate related characters and seed kin relationship edges in one call.
- **Portraits** (`Generation/Portraits/`) — `PortraitSpecBuilder` + `PortraitPromptFormatter` turn a
  character into an image-generation prompt (ancestry hint, morphology, surface detail).

---

## World, Objects & Astronomy

`GameEngineTools/World/`:

- **Time** (`World/Core/Time`, `World/Utils/Time`) — use `WDateTime`, `WDateOnly`, `WTimeOnly`,
  `WTimeSpan`, **never** `System.DateTime`. `WDateTime` is a `readonly struct` over a `long` tick
  count. Calendar-dependent properties require `WWorld.Spec`. Default calendar (Vigilia Insectianis):
  10 months × 36 days × 26 hours, configured by `FixedMonthsCalendar`.
- **Locations** (`World/Location`) — `LocationDescriptor` (noise, crowding, capacity, privacy, type)
  and `DefaultLocationService`, which computes the per-tick `InteractionSurface` and dispatches
  `ContextChanged` only to characters that moved. `WorldMap`/`WorldMapLoader` build an immutable
  location graph from CSV (or `SqliteWorldMapLoader`).
- **Objects** (`World/Objects`) — `WorldObject` (`Id`, `DisplayName`, `LocationId`,
  `WorldObjectCategory`, a list of `WorldObjectAffordance`). Providers: `StaticWorldObjectProvider`,
  `SqliteWorldObjectProvider` (+ `WorldObjectWriteBuffer`, `WorldObjectSnapshotCache`,
  `ObjectRespawnScheduler`, `PickupItemKind`). NPCs read objects as structured data only.
- **Data** (`World/Data`) — `SqliteWorldDatabase` persists world objects, the map, and social norms.
- **Astronomy** (`World/Core/Astro`) — `SunModel`/`CelestialContextComputer` produce a per-tick
  `CelestialContext` (irradiance, day length, sunrise/sunset, season, ambient temperature) from
  `AstroConfig`. When a `UniverseConfig` is also supplied, the `Universe/` Kepler stack
  (`KeplerSolver`, `OrbitalElements`, `StarPhysics`, `MoonPhysics`, `RingSystem`,
  `HabitabilityProfile`) drives season, temperature, and gravity from real planetary mechanics.
- **Simulation** (`World/Simulation`) — `SimulationScene` and the orchestrator, perception resolver,
  and speech-act/touch selectors (see below).

---

## SimulationScene

`SimulationScene` is the main loop. It owns the clock, ticks all characters in list order, routes
outcomes between characters, injects ambient/celestial context, runs the narrative formatter, and
applies LOD. It deliberately does **not** know who the player is, select social targets, or export
data.

Per-step order:

```
1. ApplyCharacterLods            — update LOD runtime per character position
2. CelestialContext compute      — sun/Kepler model → ambient temperature (if AstroConfig set)
3. LocationService.Dispatch      — emit ContextChanged to moved characters
4. OnTick callback               — scene logic, ReachOut routing (sees PREVIOUS tick's outbox)
5. Tick all characters           — full engine pipeline per character
6. RouteOutcomes                 — Interaction/Touch/SexualEncounter outcomes → initiator
7. Sleep prompts                 — per SleepPromptHandlers (NPCs auto-confirm by default)
8. Clock.Advance(dt)
9. NarrativeFormatter scan       — format domain events → OnNarrative callback
```

Selected `SimulationSceneOptions`:

| Property | Default | Effect |
|---|---|---|
| `Characters` | required | All characters; tick order = list order (convention: player at index 0). |
| `SimulationDays` | `20` | In-game days before `RunAsync()` completes. |
| `TickStep` | `0:30:00` | Clock advance per main-loop iteration. |
| `InternalSubstep` | `null` | Slices each `TickStep` into finer sub-steps for tighter latency. |
| `LocationService` | `null` | Enables per-tick `ContextChanged` dispatch. |
| `DefaultCharacterLod` / `ResolveCharacterLod` | `Nearby` / `null` | LOD selection per character. |
| `OnTick` | `null` | Scene-level callback before characters advance. |
| `SleepPromptHandlers` | `null` | Per-character sleep decision; unmapped NPCs auto-confirm. |
| `NarrativeFormatter` / `ResolveCharacter` / `OnNarrative` | `null` | Czech narrative diary pipeline. |
| `AstroConfig` / `UniverseConfig` | `null` | Sun model / planetary system; inject `CelestialContext`. |
| `ObjectSnapshotCache` / `WriteBuffer` / `RespawnScheduler` | `null` | World-object perception & lifecycle. |

The **narrative** layer (`Narrative/DefaultNarrativeFormatter`) maps domain events to Czech
`NarrativeEntry` records using `CzechWordFormComposer` from the `50PSoftware.GrammarModular.Czech`
package, returning `null` for unmapped/low-priority events.

---

## Configuration

All character config binds from `appsettings.Characters.json` under `Characters:*` via
`IOptions<T>`; `appsettings.Characters.Default.json` is the documented baseline (override per
environment). World/astronomy config binds from `appsettings.World.json` under `World:*`.

Active `Characters:*` sections:

| Section | Owns |
|---|---|
| `Physiology` | Metabolic rate, sleep-debt cap, recovery rates, injury/pain. |
| `MenstrualCycle` | Cycle length, menses, ovulation, symptom multipliers. |
| `Psychology` | Affect/stress/cognitive-load/circadian/hormonal/environment/emotion-decay. |
| `Behavior` | Decision scoring (inertia, novelty, noise penalty), intent, habits, social approach. |
| `Sleep` | Sleep scheduling, phase durations, nightmare/ambush, emergency thresholds. |
| `Interactions` | Misattribution rate and noise amplifier. |
| `Relationships` | Decay & repair, per-dimension decay multipliers, mere-exposure, transgression, attachment, Dunbar tiers, investment. |
| `Memory` | Encoding, forgetting, consolidation, distortion, reconsolidation, knowledge confidence. |
| `SemanticMemory` | Belief learning/contradiction/decay, attachment modulation, Navarro model. |
| `Goals` | Goal seeding, salience decay, frustration → abandonment. |
| `DailySchedule` | Slot bias strength, skip thresholds. |
| `Values` | Drift rate, regression-to-baseline, congruence sensitivity. |
| `SelfConcept` | Self-verification rate, discrepancy threshold for identity goals. |
| `Interests` | RIASEC drift and regression. |
| `Lod` | Behavior decision cadence per `CognitiveResolutionLevel` (Player / Nearby / Background). |
| `Fidelity` | Memory / perception / social fidelity level per LOD tier. |

`World:*` sections include `Perception`, `Astro` (sun model + latitude/seasonal amplitude), and
`Universe` (full star/planet/moon/ring definition). Each config record type lives alongside its
engine.

---

## DI Registration

`Characters/Hosting/ServiceCollectionExtensions.cs`. The shorthand registers all nine pipeline
engines at once (Values, SelfConcept, Interests, Goal and the support services come from
`AddCharactersCore`):

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
```

Each `Add*Engine<T>()` method binds its `IOptions<TConfig>` automatically (overridable via a
lambda). Related registrations:

```csharp
services.AddObjectInteractionEngine();   // optional object-interaction subsystem
services.AddCharacterGeneration(spec);   // or the lazy Func<IServiceProvider, HumanBlueprintSpec> overload
services.AddFamilySystem();              // FamilyGraph + NuclearFamilyGenerator (after AddCharacterGeneration)
```

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

EngineTests/        ← MSTest suite (build + run target)
GameSandbox/        ← Console simulation runner (scene wiring, narrative loop)
CharacterGenerator/ ← Interactive character-creation CLI
LogsResolver/       ← WPF JSONL log viewer (+ LogsResolverTests)
RelationshipsGame/  ← WPF prototype
```
