# GameEngineTools (GET)

> **White-box autonomous NPC behavior simulation engine — C# / .NET 8**
> © 50PSoftware

![Language](https://img.shields.io/badge/language-C%23-239120?logo=csharp&logoColor=white)
![Framework](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)
![License](https://img.shields.io/badge/license-Proprietary-red)

GET is a research-grade simulation platform that exposes the full internal state of characters for study and iteration. Unlike commercial titles, GET is not a presentation layer — it is a scientific sandbox for modeling human psychology, physiology, memory, relationships, and social behavior.

---

## Table of Contents

1. [Architecture Overview](#architecture-overview)
2. [Engine Pipeline](#engine-pipeline)
3. [Engines](#engines)
   - [PhysiologyEngine](#physiologyengine)
   - [PsychologyEngine](#psychologyengine)
   - [BehaviorEngine](#behaviorengine)
   - [SleepCoordinator](#sleepcoordinator)
   - [InteractionEngine](#interactionengine)
   - [RelationshipsEngine](#relationshipsengine)
   - [MemoryEngine](#memoryengine)
   - [SemanticMemoryEngine](#semanticmemoryengine)
4. [Character Generation](#character-generation)
5. [SimulationScene](#simulationscene)
6. [World & Locations](#world--locations)
7. [Configuration Reference](#configuration-reference)
   - [Characters:Physiology](#charactersphysiology)
   - [Characters:MenstrualCycle](#charactersmenstrualcycle)
   - [Characters:Psychology](#characterspsychology)
   - [Characters:Behavior](#charactersbehavior)
   - [Characters:Sleep](#characterssleep)
   - [Characters:Interactions](#charactersinteractions)
   - [Characters:Relationships](#charactersrelationships)
   - [Characters:Memory](#charactersmemory)
   - [Characters:SemanticMemory](#characterssemanticmemory)
   - [Characters:Lod](#characterslod)
   - [Characters:Fidelity](#charactersfidelity)
8. [Project Structure](#project-structure)

---

## Architecture Overview

GET models each NPC as an `OrchestratedHuman` — a character that runs a fixed multi-engine pipeline on every simulation tick. All internal state is exposed via `EnginesSnapshot`, making it suitable for debugging, visualization, and research.

Key frameworks used internally:

| Framework | Purpose |
|---|---|
| PAD emotional model | Valence / Arousal / Dominance state |
| Big Five (OCEAN) | Personality generation and behavioral modulation |
| Ebbinghaus memory decay | Episodic memory forgetting curve |
| Dunbar time-budget model | Relationship tier capacity management |
| 2D Anxiety × Avoidance | Attachment style (continuous, not categorical) |
| Williams four-need-threat | Rejection effects on belonging, control, self-esteem, meaningful existence |
| Lewicki et al. | Apology component weights for repair mechanics |

---

## Engine Pipeline

Each tick runs in three phases:

```
Phase A  ──  HandleScheduled + HandleInbox
                 (actions delivered against the previous snapshot)

Phase B  ──  Physiology → Psychology → [RefreshSnapshot]
             → Behavior (cadence policy) → Interactions
             → Relationships → Memory → SemanticMemory
                 (engines advance; events accumulate in outbox)

             [RefreshSnapshot]

Phase C  ──  SelfDeliver (max 8 passes)
             → RefreshSnapshot → PublishOutbox
```

The mid-tick `RefreshSnapshot` between Psychology and Behavior is intentional: Behavior reads the **current tick's** physiological and psychological state, not last tick's.

DI registration:

```csharp
services.AddCharacters<
    DefaultPhysiologyEngine,
    DefaultPsychologyEngine,
    DefaultBehaviorEngine,
    DefaultInteractionEngine,
    DefaultRelationshipsEngine,
    DefaultMemoryEngine,
    DefaultSemanticMemoryEngine>();
```

Config sections bind automatically from `appsettings.json` under `Characters:*`.

---

## Engines

### PhysiologyEngine

**State:** `PhysiologyState` — Energy, Hunger, Thirst, Pain, ImmuneLoad, BodyTempDelta, SleepDebtHours, MenstrualCycle, NutritionState, InjuryState, PostpartumState

**What it does:**
Models the body's physical condition. Each tick it advances biological needs (hunger, thirst, energy depletion) and applies recovery. It owns the menstrual cycle simulation — advancing cycle day, detecting phase transitions, and setting symptom flags (pain, bloating, breast tenderness, PMS/PMDD). It also models injury healing, postpartum recovery, and nutrition tracking (calories, VitaminD, iron, protein).

**Reads:** World time delta, character birth date and biology.

**Emits:**
- `InjuryReceived` / `InjuryHealed` — when injury state changes
- `PostpartumPhaseChanged` — when recovery phase advances
- `PregnancyStarted` / `PregnancyDiscovered` / `ChildBorn` — reproductive events

**Why it runs first:** All other engines (especially Psychology) read physiological state. Pain, hunger, and sleep debt are upstream inputs to mood, stress, and decision-making.

---

### PsychologyEngine

**State:** `PsychologyState` — Valence, Arousal, Dominance (PAD), Stress, CognitiveLoad, DominantEmotion, MoodBaseline, MotivationState

**What it does:**
Models the affective state using the PAD model. Each tick it:
1. Decays Stress toward zero at `StressRecoveryRatePerHour`
2. Drifts PAD dimensions toward their resting neutral values
3. Applies physiology modulation (pain → stress, sleep debt → CogLoad, fever → arousal suppression, hunger/thirst → valence penalties)
4. Applies circadian arousal rhythm (peaks at `CircadianArousalPeakHour`, troughs at `CircadianArousalTroughHour`)
5. Applies hormonal coupling (cortisol, testosterone)
6. Applies environmental effects (noise, crowding, temperature, isolation, privacy)
7. Adds random daily affect noise scaled by `BaselineAffectVariance`
8. Infers `DominantEmotion` from the current PAD coordinates via a rule table
9. Decays each discrete emotion at its own rate (fear/surprise fast; shame/sadness very slow)
10. Checks for stress manifestation (if stress exceeds `StressManifestationThreshold` for `StressManifestationHours`, emits `StressManifested`)

**Manifestation types** depend on personality: high Neuroticism → anxiety/rumination; low Agreeableness → aggression; high Openness → creativity channel; otherwise → withdrawal.

**Reads:** `PhysiologyState` (from mid-tick snapshot), personality BigFive, location context.

**Emits:** `MotivationChanged`, `StressSpiked`, `StressManifested`

---

### BehaviorEngine

**State:** `BehaviorState` — NeedRest, NeedFood, NeedWater, NeedBelonging, NeedCompetence, NeedIntimacy, CurrentPlan, Cooldowns, ActiveIntent, HabitTraces, SleepDeclineCount

**What it does:**
The decision-making core. Each tick it:
1. Recomputes all six needs from physiological and psychological state
2. Scores all candidate actions via a utility function
3. Applies inertia boost to the current action (`InertiaWeight`) and novelty penalty for category switching (`NoveltyPenalty`)
4. Applies habit bias from `HabitTraces` — repeated behavior in matching cue contexts gets a multiplier and flat bonus
5. Applies prestige/dominance modulation to social action candidates
6. Selects the highest-utility action and emits `ActionCommitted`
7. Manages intent stability via `DefaultIntentManagementEngine` — prevents flickering between similar-utility actions
8. Delegates sleep lifecycle to `DefaultSleepCoordinator`

**Habit learning** (`BehaviorHabitLearning`): After each `ActionCommitted`, a learning signal is built from cue kind, need relief, and coping reinforcement. Habit traces strengthen (`HabitLearningRate`), decay daily (`HabitDecayPerDay`), and are pruned when `MaxHabitTraces` is exceeded. Traces can be classified as Adaptive, Neutral, or MaladaptiveCoping.

**Reads:** Full `EnginesSnapshot` (physiology, psychology, relationships, memory working set).

**Emits:** `ActionCommitted`, `InteractionProposed` (from social targeting candidates), `SleepPromptRequested`

**Config sections:** `Characters:Behavior`, `Characters:Sleep`

---

### SleepCoordinator

**Owned by:** `DefaultBehaviorEngine` (via `DefaultSleepCoordinator`)

**What it does:**
Manages the full sleep session lifecycle as a state machine:

```
Awake → FallingAsleep → LightSleep → DeepSleep → REM → Awake
```

- Emits phase transition events as the session advances through phases
- Rolls nightmare probability based on stress level
- Rolls ambush probability for outdoor sleep (modified by companion guard)
- Applies memory consolidation boost (`SleepConsolidationBoost`) at the end of REM
- Tracks `SleepDeclineCount` and applies `DeclinePenaltyStressPerHour` when sleep is overdue
- Emergency sleep threshold overrides intent when `NeedRest > EmergencyNeedRestThreshold` or `Energy < EmergencyEnergyThreshold`
- Blocks sleep when hunger or thirst exceed their respective block thresholds

**Emits:** `SharedSleepBegan`, `SleepEnded`, `NightmareTriggered`, `SleepInterrupted`, `SleepPromptRequested`

**EventIds:** 1100–1113

---

### InteractionEngine

**State:** `InteractionSurface` — Location, HasPrivacy, Noise, Crowding, Kind (Social/Private/Work/Rest/Public), ProxemicDistanceMeters

**What it does:**
Evaluates proposed social interactions and decides acceptance. For each `InteractionProposed`:
1. Reads relationship edge (Closeness, Comfort, Trust, ResponsiveDesireLevel)
2. Reads psychological state (Valence, Stress)
3. Reads environment (privacy, noise, crowding)
4. Computes acceptance probability — closer relationships, better mood, privacy, and low noise all increase it
5. Applies misattribution penalty — high stress + noise causes the character to misread intent (`MisattributionRateBase`, amplified by `NoiseAttributionAmplifier`)
6. Computes peak-end valence for the memory engine (peak-end rule)
7. If the interaction was an accepted `Invite` and relationship readiness thresholds are met, emits `SexualEncounterProposed`

**Sexual encounter readiness gate:** Trust ≥ 72, Comfort ≥ 72, Closeness ≥ 70, SexualInterest ≥ 68, privacy required, pain < 55, energy ≥ 25, stress < 70, character is adult.

Also handles `TouchAttempted` — evaluates physical touch acceptance separately with sociosexuality and Closeness guards.

**Reads:** `RelationshipEdge` per target, `PsychologyState`, `InteractionSurface`.

**Emits:** `InteractionOutcome`, `TouchOutcome`, `SexualEncounterProposed`, `SexualEncounterOutcome`

**EventIds:** 1200–1202

---

### RelationshipsEngine

**State:** `RelationshipState` — dictionary of `RelationshipEdge` per `HumanId`

**Each `RelationshipEdge` tracks:**
Trust, Respect, Closeness, Like, Comfort, Attraction, RomanticInterest, SexualInterest, Familiarity, PerceivedDominance, PerceivedPrestige, TransgressionResidue, ResponsiveDesireLevel, PositiveInteractionCount, DomainBreakdown (Humor/Intellect/Values/Physical/Aesthetics)

**What it does:**
Maintains the full social graph and reacts to every interaction event:

- **First impression** — lerps relationship dimensions 70% toward the first impression values; seeds Like via halo effect
- **Accepted interaction** — updates Trust (SelfDisclosure, Validation, Meta only), Respect (Validation, Question, Meta), Closeness (communal growth on intimate acts), Familiarity (mere-exposure logarithmic curve up to `MereExposureSaturation`), and the relevant `DomainBreakdown` dimension per `SpeechAct`
- **Rejected interaction** — applies half-strength domain update; can accumulate TransgressionResidue
- **MicroPositive / MicroNegative** — small incremental boosts or penalties to Like and Comfort
- **RepairAttempt** — accepted: reduces TransgressionResidue by `RepairGain`; rejected: adds `RupturePenalty × 0.5`
- **SexualEncounterOutcome** — accepted: boosts Comfort, Closeness, RomanticInterest, SexualInterest; also runs `AttractionPlasticity`
- **Daily decay** — all dimensions decay at `DecayPerDay × DecayMultiplier[dimension]`; decay accelerates when contact gap exceeds `ExpectedContactIntervalDays` (Navarro gap effect)
- **Dunbar tier pressure** — when Tier 1 or Tier 2 capacity is exceeded, decay multipliers increase proportionally
- **Familiarity-Like dissonance** — high Familiarity with neutral/negative history causes Like to drift down (`FamiliarityLikeDissonancePenalty`)

**Attachment modulation** via `RelationalStabilization` and `RejectionStingMultiplier`: personality profile (Agreeableness, Neuroticism, attachment style) modulates how strongly decay, rejection, and repair effects hit.

**EventIds:** 2001–2007

---

### MemoryEngine

**State:** `MemoryIndex` — list of `EpisodicMemory`, plus `Knowledge` (dictionary of `SemanticFact`)

**Each `EpisodicMemory` tracks:**
What (canonical key), PerceivedWhat (subjective recall), When, Salience, Strength, Emotion, Distortion, RecallConfidence, OtherPerson, BeliefEvidence

**What it does:**
Manages episodic memory encoding, retrieval, consolidation, and forgetting:

- **Encode** — on relevant domain events, builds a `What` key via `MemoryWhatFactory`, computes salience from emotional intensity, and checks for an existing episode with the same key to reinforce (`ReinforcementBoost`) instead of creating a duplicate
- **Forgetting** — each tick applies Ebbinghaus decay: strength reduces by `ForgettingRate`; episodes below `PruneThreshold` are removed
- **Sleep consolidation** — at `SleepEnded`, recent episodes receive a `SleepConsolidationBoost` to strength
- **Stress distortion** — high stress at encoding shifts `Distortion` upward (`StressDistortionWeight`), degrading `RecallConfidence`
- **Reconsolidation drift** — each recall applies a small `ReconsolidationDriftRate` to PerceivedWhat, modeling memory malleability
- **Recall** — `MemoryCognition.Recall()` returns episodes filtered by query, with retrieval quality degraded when `CognitiveBurden` exceeds `CognitiveBurdenThreshold`
- **DecisionWorkingSet** — builds a `ReflectionSummary` for the behavior engine: mood tendency, dominant social memory, recent significant events
- **Knowledge** — stores `SemanticFact` entries with confidence levels; direct witness confidence = `DirectWitnessConfidence`, gossip = `GossipConfidence`; knowledge decays at `KnowledgeConfidenceDecayPerDay` and is pruned below `KnowledgePruneThreshold`

**What string schema:** `{Category}:{Type}:{Outcome}|key=value|key=value`  
Example: `Interaction:SmallTalk:Accepted|from=a3f2c1d0|to=b7e9a2f1`  
The `What` string is the reinforcement key — identical events reinforce rather than duplicate.

**Emits:** `MemoryEncoded`, `MemoryConsolidated`

**EventIds:** 3000–3001

---

### SemanticMemoryEngine

**State:** `SemanticMemoryState` — dictionary of `PersonBeliefSet` per `HumanId`

**Each `PersonBeliefSet` tracks five belief dimensions:**
Warm, EmotionallySafe, Reliable, Rejecting, Critical — each with Strength and Stability

**What it does:**
Processes `MemoryEncoded` events and builds generalized person-beliefs from episodic patterns. This is the character's abstract model of *who other people are*:

- **Pattern detection** — examines the last `PatternWindowSize` episodes for a given person; if at least `MinimumPatternSupport` episodes match a belief pattern, the belief is updated
- **Learning** — confirming evidence strengthens belief by `LearningRate` and increases Stability by `StabilityGainPerEvidence`
- **Contradiction** — contradicting evidence weakens belief by `ContradictionRate` and reduces Stability by `ContradictionStabilityHit`; high-stability beliefs resist contradiction
- **Decay** — all beliefs decay passively at `DecayPerDay`
- **Attachment modulation** — anxious attachment amplifies learning and contradiction sensitivity; avoidant attachment suppresses learning, especially for EmotionallySafe; disorganized attachment is most destabilized by contradictions
- **Navarro toxic pattern** — when the ratio of negative-to-positive episodes exceeds `NavarroCriticalMultiple`, decay accelerates by `NavarroDecayAccelerator` (rapid disillusionment)

**Feeds into:**
- `SemanticMath.ExpectedAcceptance()` — probability that a person will accept a ReachOut, used by social targeting
- `SemanticTargeting.RankTargets()` / `ChooseTarget()` — selects the best social approach target
- `SocialTargetCandidateFactory` — generates `ReachOut` and `InviteIntimacy` behavior candidates with `SocialTargetingData` (expectedAcceptance, vulnerabilitySafety, rejectionRisk)

---

## Character Generation

Before a character can be simulated it must be generated. The generation pipeline is a separate, stateless subsystem that produces a `HumanBlueprint` — a snapshot of all stable traits. The `DefaultHumanFactory` then wraps the blueprint into a live `OrchestratedHuman` with a full engine stack.

### Life stages (StadiumType)

All generators are stadium-aware. The `StadiumResolver` maps character age to a `StadiumType`:

| Stadium | Age range | Notes |
|---|---|---|
| `Baby` | 0–2 | No sexual dimension, very high Neuroticism, low Conscientiousness |
| `Child` | 3–11 | No sexual dimension, curiosity-driven, Conscientiousness still forming |
| `Teenager` | 12–17 | High Neuroticism variance (puberty volatility), max Sociosexuality = Intermediate |
| `Adult` | 18–39 | Default — no adjustments |
| `MidAged` | 40–64 | Lower Neuroticism, higher Conscientiousness |
| `Old` | 65+ | Lower Openness and Sexuality, higher Conscientiousness, aging surface detail |

Thresholds can be overridden via `StadiumThresholds` for game worlds with different age conventions.

---

### PersonalityGenerator

Generates a `Personality` from a `PersonalitySpec` + optional `PersonalityHints`.

**BigFive generation:**
- Each trait is drawn from a `TraitDistribution(Mean, Dev, Skew, Concentration)` using inverse-normal CDF with delta-method skew correction
- Correlations between traits are applied via Cholesky decomposition of the 5×5 correlation matrix
- Default realistic correlations: C↔N = −0.35, E↔N = −0.20, O↔C = +0.12

**Default population-level trait correlations:**

| | O | C | E | A | N |
|---|---|---|---|---|---|
| **O** | 1.00 | +0.12 | +0.12 | +0.15 | −0.12 |
| **C** | | 1.00 | +0.10 | +0.10 | **−0.35** |
| **E** | | | 1.00 | +0.08 | **−0.20** |
| **A** | | | | 1.00 | −0.20 |
| **N** | | | | | 1.00 |

**MotivationWeights** are derived from BigFive via a linear mapping (`MotivationMapping`):  
`weight = Bias + wO·O + wC·C + wE·E + wA·A + wN·N`, clamped to [0, 1].  
Example: Affiliation is driven by Extraversion (+0.25) and Agreeableness (+0.20); Achievement by Conscientiousness (+0.30).

**Categorical traits** (Attachment, CommunicationStyle, Chronotype, Sociosexuality) are sampled from weighted distributions, also stage-calibrated.

**Hard constraints via PersonalityHints:**

| Stadium | Constraint |
|---|---|
| Baby | Sociosexuality = Restricted, Communication = Direct |
| Child | Sociosexuality = Restricted |
| Teenager | Sociosexuality ≤ Intermediate |
| Adult+ | No hard constraints |

---

### AppearanceGenerator

Generates a `PhysicalAppearance` from a seed + `StadiumType` + `SexBiology`. Fully deterministic when a fixed seed is provided.

**Generation pipeline:**
1. **BodyLatent** — height sampled from `(HeightFemale/Male)` range; frame, body latent factors computed
2. **FaceLatent** — correlated to body (broader frame → different facial proportions)
3. **Body morphology** — shoulder/hip breadths correlated to height and frame; sexual dimorphism enforced via `SexBiasStrength`
4. **Face morphology** — nose prominence, lip fullness, mandible angle, nasolabial angle, facial asymmetry — all from `MorphologyGenerationSpec`
5. **Surface traits** — skin oiliness, acne, wrinkle tendency, scar probability — aging factor drives surface detail rate
6. **Colors** — SkinTone, EyeColor, HairColor, HairType sampled from weighted distributions
7. **DistinctiveMarks** — derived from surface thresholds (mole patterns, freckles, scars)

**Stadium-specific morphology parameters (`MorphologyGenerationSpec.For()`):**

| Parameter | Baby | Child | Teenager | Adult |
|---|---|---|---|---|
| `JitterAmplitude` | 0.06 | 0.10 | 0.10 | 0.10 |
| `SexBiasStrength` | 0.10 | 0.10 | 0.28 | 0.28 |
| `Juvenility` latent | 0.95 | 0.75 | 0.35 | 0.15 |
| `AgingFactor` latent | 0.0 | 0.0 | 0.0 | 0.0 |

Height ranges by stadium (female / male in cm):

| Stadium | Female | Male |
|---|---|---|
| Baby | 45–90 | 45–92 |
| Child | 90–148 | 90–150 |
| Teenager | 148–168 | 150–178 |
| Adult (default) | 155–175 | 165–185 |

---

### AttractionCalculator

`DefaultAttractionCalculator` computes how much observer A finds target B attractive. Attraction is **asymmetric** — called per-pair on demand (e.g. at `FirstImpressionFormed`).

**Score components (sum = 100 max):**

| Component | Max | What it captures |
|---|---|---|
| `BasePhysical` | 40 | Evolutionary signals: WHR approximation (shoulder/hip ratio), height in population range, facial symmetry proxy (nose + lip near 0.5) |
| `PreferenceMatch` | 35 | Personal taste: height preference match, frame preference, WHR preference from observer's `AttractionProfile` |
| `StateModifier` | −15 to +10 | Current state: posture (+), acne (−), bloating (−) from `AppearanceView` |
| `MereExposure` | 15 | Familiarity bonus from repeated positive contact (from `RelationshipEdge`) |

**First impression Like** (halo effect):  
`Like = 25 + Attraction × 0.40 + ObserverValence × 8`, clamped to [0, 100].  
At Attraction 50 → Like ≈ 45; at Attraction 80 → Like ≈ 57; at Attraction 20 → Like ≈ 33.

---

### AttractionProfileGenerator

Generates an `AttractionProfile` for each character — their personal physical preferences:
- `PreferredHeightCm` — sampled with sexual dimorphism bias (women prefer taller men; men prefer slightly shorter women)
- `HeightToleranceCm` — how wide the acceptable height range is
- `PreferredWhr` — preferred waist-hip ratio
- `FramePreference` — body frame preference (None / Petite / Medium / Large / Strong)
- `SexualOrientation` — Heterosexual / Homosexual / Bisexual / Asexual
- `TargetAttractionWeights` — per-biology attraction weights derived from orientation

---

### ChildBlueprintGenerator

Generates a newborn `HumanBlueprint` from two parent `IHuman` instances. Blends parent traits (BigFive, appearance latent factors) with Baby-stage baseline and deterministic variation. Used when `ChildBorn` is emitted by the physiology engine.

---

## SimulationScene

`SimulationScene` is the main simulation loop. It owns the clock, ticks all characters in order, routes outcomes between characters, and handles sleep prompts.

**What the scene does — not what it doesn't:**

| Does | Does not |
|---|---|
| Ticks characters in `Characters` list order | Know who is the player vs NPC |
| Routes `InteractionOutcome` to initiator | Route `ReachOut → InteractionProposed` (caller's responsibility) |
| Handles sleep prompts per `SleepPromptHandlers` | Export data or print headers |
| Dispatches `ContextChanged` via `LocationService` | Select social targets |
| Injects `CelestialContext` when `AstroConfig` set | |
| Runs `NarrativeFormatter` + `OnNarrative` callback | |
| Applies LOD per character via `ApplyCharacterLods` | |

**Tick order within one step:**
```
1. ApplyCharacterLods          — update LOD runtime per character position
2. CelestialContext compute     — sun model → ambient temperature (if AstroConfig set)
3. LocationService.Dispatch     — emit ContextChanged to moved characters
4. OnTick callback              — scene-level logic, ReachOut routing
                                  (LastOutbox here = PREVIOUS tick's outbox)
5. Tick all characters          — all engines advance
6. RouteOutcomes                — InteractionOutcome/Touch/SexualEncounter → initiator
7. Sleep prompts                — per SleepPromptHandlers (default: auto-confirm for NPCs)
8. Clock.Advance(dt)
9. NarrativeFormatter scan      — format narrative entries → OnNarrative
```

If `InternalSubstep` is set, the outer `TickStep` is sliced into finer sub-steps for better character-to-character latency and timing accuracy.

### SimulationSceneOptions

| Property | Default | Effect |
|---|---|---|
| `Characters` | required | All characters in the scene. Tick order = list order; convention: player at index 0. |
| `SimulationDays` | `20` | How many in-game days the scene runs before `RunAsync()` completes. |
| `TickStep` | `0:30:00` | Outer tick step — how far the clock advances per main loop iteration. |
| `InternalSubstep` | `null` | When set, each `TickStep` is divided into sub-steps of this size. Finer granularity, more CPU. |
| `LocationService` | `null` | When provided, `DispatchContextEvents` is called before each tick. |
| `DefaultCharacterLod` | `Nearby` | Fallback LOD when `ResolveCharacterLod` returns nothing. |
| `ResolveCharacterLod` | `null` | Lambda: `(IHuman) → CognitiveResolutionLevel`. Used by `SceneCharacterLodResolver`. |
| `OnTick` | `null` | Callback invoked each tick before characters advance. Receives `(WDateTime now, IReadOnlyList<IHuman> chars)`. |
| `SleepPromptHandlers` | `{}` | Per-character sleep decision. Key = `HumanId`, value = `Func<SleepPromptRequested, bool>`. NPCs not in the dict auto-confirm sleep. |
| `NarrativeFormatter` | `null` | Formats domain events into `NarrativeEntry` text. Pass `new DefaultNarrativeFormatter()` to enable diary. |
| `ResolveCharacter` | `null` | Lambda: `HumanId → NarrativeCharacterInfo` for the narrative formatter (name + gender for grammar). |
| `OnNarrative` | `null` | Callback receiving each formatted `NarrativeEntry`. Store to a diary list or show in UI. |
| `AstroConfig` | `null` | Sun model configuration. When set, `CelestialContext` (ambient temperature, light) is injected each tick. |
| `UniverseConfig` | `null` | Planetary system (Phase 2). When set together with `AstroConfig`, Kepler mechanics drive season, temperature, and gravity. |

---

## World & Locations

### LocationDescriptor

Describes a named place in the world. The `InteractionEngine` reads noise, crowding, and privacy from `InteractionSurface`, which is computed by `DefaultLocationService` from these descriptors.

| Field | Type | Effect |
|---|---|---|
| `Id` | `string` | Unique identifier used in `MoveCharacter` and `GetLocation`. |
| `DisplayName` | `string` | Human-readable name used in narrative output. |
| `Type` | `LocationType` | Social / Private / Work / Rest / Public — drives `SurfaceKind` and `MoveTo:*` action routing. |
| `BaseNoise` | `double [0–1]` | Ambient noise before any characters arrive. Library: 0.05; smithy: 0.70. |
| `NoisePerPerson` | `double [0–1]` | Additional noise per character present. Formula: `BaseNoise + NoisePerPerson × count`, clamped to 1. |
| `Capacity` | `int` | "Comfortable" capacity. Crowding = `characterCount / Capacity`, clamped to 1. |
| `AllowsPrivacy` | `bool` | Whether privacy is ever possible here. A public square is `false` regardless of character count. |

**LocationType values:**

| Value | Use case |
|---|---|
| `Social` | Tavern, village square, market — open social space |
| `Private` | Library, private room, study — intimate or focused |
| `Work` | Workshop, forge, fields — tied to productive activity |
| `Rest` | Inn, home — recovery space |
| `Public` | Roads, large plazas — no dominant character |

---

### DefaultLocationService

Tracks character positions and computes `InteractionSurface` per location. Called by `SimulationScene` once per tick via `DispatchContextEvents`.

**Noise formula:** `BaseNoise + NoisePerPerson × characterCount`, clamped to [0, 1].  
**Crowding formula:** `characterCount / Capacity`, clamped to [0, 1].  
**Privacy:** `AllowsPrivacy && characterCount <= 1` — a place with two or more people is no longer private even if it allows it.

Only emits `ContextChanged` to characters whose location has changed since the last dispatch — not to every character every tick. Pass `forceAll: true` on the first tick to initialize everyone.

---

### WorldMap & WorldMapLoader

`WorldMap` is an immutable graph of locations loaded from CSV at startup.

**`Locations.csv` columns (semicolon-separated):**
```
Id ; DisplayName ; Type ; Region ; BaseNoise ; NoisePerPerson ; Capacity ; AllowsPrivacy
```

**`Connections.csv` columns:**
```
FromId ; ToId ; TravelMinutes
```

Connections are directed edges in the adjacency graph. `WorldMap.GetNeighbors(locationId)` returns adjacent locations sorted by travel time.

`WorldMap.RegisterAllLocations(locationService)` bulk-registers all loaded locations into a `DefaultLocationService` — call once at startup instead of manual `RegisterLocation` calls.

Locations can be queried by region: `worldMap.GetLocationsInRegion("Castle")` returns all location IDs tagged with that region in `Locations.csv`. Region is world-level metadata only; it is not stored in `LocationDescriptor`.

---

## Configuration Reference

All configuration lives in `appsettings.Characters.json` (or `appsettings.Characters.Default.json` as the baseline). Override any value in environment-specific files.

---

### `Characters:Physiology`

| Key | Default | Effect |
|---|---|---|
| `RestingMetabolicRate` | `1600.0` | Base caloric need per day. Affects how quickly hunger rises when nutrition is insufficient. |
| `MaxSleepDebtHours` | `12.0` | Cap on accumulated sleep debt. Beyond this, cognitive load and stress spike hard. |
| `EnableMenstrualCycle` | `true` | Whether female characters run menstrual cycle simulation. Set `false` for simplified worlds. |
| `MenstrualCycleBeginsInAge` | `12` | Minimum age (years) at which the cycle can start. |
| `EnergyRecoveryPerSleepHour` | `10` | Energy points recovered per hour of sleep. Higher = characters bounce back faster. |
| `PainPassiveRecoveryPerHour` | `0.3` | Pain reduction per hour while awake. Low value = injuries linger realistically. |
| `PainSleepRecoveryPerHour` | `0.5` | Pain reduction per hour during sleep. Sleep accelerates healing. |

---

### `Characters:MenstrualCycle`

Only active when `EnableMenstrualCycle = true` and character biology is female.

| Key | Default | Effect |
|---|---|---|
| `MeanCycleLengthDays` | `28` | Average cycle length. Actual length is sampled with Gaussian noise. |
| `VariabilityDaysStdDev` | `2.0` | Standard deviation of cycle length. Higher = more irregular cycles. |
| `MensesMeanDays` | `5` | Average duration of menstruation in days. |
| `OvulationDayOfCycle` | `14` | Day on which ovulation occurs (feeds SemanticTargeting and attraction modulation). |
| `MinCycleLengthDays` | `21` | Hard minimum to prevent biologically implausible short cycles. |
| `MaxCycleLengthDays` | `35` | Hard maximum to clamp the sampled cycle length. |
| `PmsRisk` | `0.35` | Probability (0–1) that a character experiences PMS symptoms in a given cycle. |
| `EnableOvulationWindowEvents` | `true` | Whether the engine emits events during the ovulation window. |
| `EnableSymptoms` | `true` | Whether physical symptoms (pain, bloating, breast tenderness) are simulated. |
| `PainBaseMultiplier` | `1.0` | Scales menstrual pain intensity. `2.0` = dysmenorrhea-level pain. |
| `BloatBaseMultiplier` | `1.0` | Scales bloating severity. Affects comfort and social willingness. |
| `BreastTenderMultiplier` | `1.0` | Scales breast tenderness. Affects touch acceptance threshold. |

---

### `Characters:Psychology`

#### Core affect

| Key | Default | Effect |
|---|---|---|
| `BaselineAffectVariance` | `0.02` | Random noise added to Valence each tick. Higher = mood fluctuates more. |
| `StressRecoveryRatePerHour` | `1.5` | Stress reduction per hour under normal conditions. Low = chronic stress accumulates. |
| `SleepQualityAffectWeight` | `0.5` | How much sleep quality shifts Valence the next morning. |
| `MoodBaselineRecoveryPerHour` | `0.5` | Rate at which MoodBaseline drifts back toward neutral (50). |
| `MoodBaselineHighStressThreshold` | `80.0` | Stress above this suppresses MoodBaseline recovery entirely. |
| `MoodBaselineAgreeablenessBonus` | `0.3` | Extra MoodBaseline recovery per unit of Agreeableness above 0.5. |

#### Circadian rhythm

| Key | Default | Effect |
|---|---|---|
| `EnableCircadianRhythm` | `true` | Enables time-of-day modulation of Arousal. |
| `CircadianArousalPeakHour` | `14.0` | Hour (0–23) when Arousal is naturally highest. |
| `CircadianArousalTroughHour` | `3.0` | Hour when Arousal is naturally lowest (post-midnight dip). |
| `CircadianInfluence` | `0.15` | Amplitude of circadian Arousal swing. `0.0` = flat. |

#### Cognitive load

| Key | Default | Effect |
|---|---|---|
| `CognitiveLoadSleepDebtWeight` | `1.8` | CognLoad added per hour of sleep debt. Exhausted characters can't think straight. |
| `CognitiveLoadPainWeight` | `0.4` | CognLoad added per pain point. Pain distracts decision-making. |
| `CognitiveLoadStressWeight` | `0.3` | CognLoad added per stress point. Stress narrows cognitive bandwidth. |
| `CognitiveLoadRecoveryPerHour` | `5.0` | CognLoad reduction per hour of rest or low-stimulation. |
| `FeverCognitiveLoadPerDegree` | `8.0` | CognLoad added per degree of body temperature above normal. |
| `FeverArousalSuppressPerDegree` | `0.04` | Arousal suppressed per degree of fever. Sick characters are lethargic. |

#### Stress manifestation

| Key | Default | Effect |
|---|---|---|
| `StressManifestationThreshold` | `70.0` | Stress must exceed this value… |
| `StressManifestationHours` | `4.0` | …for this many in-game hours before `StressManifested` is emitted. |

Manifestation type is personality-driven: high Neuroticism → anxiety/rumination; low Agreeableness → aggression; high Openness → creativity channel; otherwise → withdrawal.

#### Sickness behavior (immune coupling)

| Key | Default | Effect |
|---|---|---|
| `SicknessAnhedoniaImmuneThreshold` | `50.0` | ImmuneLoad above this triggers reward blunting (anhedonia). |
| `SicknessAnhedoniaRewardBlunting` | `0.5` | Fraction by which Valence gains are reduced during illness. |
| `SicknessLethargyArousalPenalty` | `0.008` | Arousal reduction per tick of high ImmuneLoad. Sick = tired. |
| `SicknessBrainFogCogLoadBonus` | `3.0` | Extra CognLoad per tick when immune system is active. |

#### Hormonal coupling

| Key | Default | Effect |
|---|---|---|
| `CortisolStressWeight` | `0.15` | How strongly cortisol level drives Stress. |
| `CortisolArousalWeight` | `0.008` | How strongly cortisol raises Arousal (alertness under threat). |
| `TestosteroneIntimacyWeight` | `0.3` | How strongly testosterone boosts NeedIntimacy. |
| `TestosteroneStressResilienceWeight` | `0.008` | How much testosterone reduces Stress response. |

#### Emotion decay (points per hour; lower = emotion lingers longer)

| Key | Default | Character |
|---|---|---|
| `EmotionDecayFear` | `3.0` | Fast — dissipates once threat is gone |
| `EmotionDecaySurprise` | `3.0` | Fast — momentary |
| `EmotionDecayDisgust` | `2.5` | Moderate-fast |
| `EmotionDecayJoy` | `1.0` | Moderate |
| `EmotionDecayPride` | `0.8` | Slow — pride lingers |
| `EmotionDecayTenderness` | `0.7` | Slow |
| `EmotionDecayAnger` | `0.6` | Slow — anger is sticky |
| `EmotionDecayShame` | `0.4` | Very slow — shame persists |
| `EmotionDecaySadness` | `0.06` | Extremely slow — grief model |

#### Environment effects

| Key | Default | Effect |
|---|---|---|
| `NoiseStressThreshold` | `0.55` | Noise level (0–1) above which stress starts accumulating. |
| `NoiseStressWeightPerHour` | `0.08` | Stress added per hour above threshold. |
| `HomeNoiseStressMultiplier` | `0.4` | Noise at home is less stressful (familiarity effect). |
| `ProxemicsIntimateZoneStressPerHour` | `4.0` | Stress from unwanted physical closeness. |
| `ProxemicsPersonalZoneStressPerHour` | `1.5` | Stress from personal space invasion. |
| `PrivacyMismatchStressWeight` | `6.0` | Stress when character needs privacy but is in a public space. |
| `IsolationStressWeight` | `3.0` | Stress when character needs social contact but is alone. |
| `PrivacyRecoveryBonusPerHour` | `0.8` | Stress recovery bonus per hour of sought-after solitude. |
| `AmbientTempHeatThreshold` | `27.0` | Temperature (°C) above which heat stress begins. |
| `AmbientTempColdThreshold` | `15.0` | Temperature below which cold discomfort begins. |

---

### `Characters:Behavior`

#### Decision scoring

| Key | Default | Effect |
|---|---|---|
| `InertiaWeight` | `0.25` | Utility multiplier bonus for repeating the current action. `0.0` = no routine; `0.5` = strong habit effect. |
| `NoveltyPenalty` | `0.1` | Utility penalty for switching to a different action category (cognitive switching cost). |
| `PlanningHorizonHours` | `2.0` | How far ahead the engine considers planned actions. |
| `NoiseCognitivePenaltyMax` | `0.45` | Maximum utility reduction for Work/Create at Noise = 1.0. Zero penalty below `NoiseStressThreshold`. |

#### Sleep scheduling

| Key | Default | Effect |
|---|---|---|
| `BaseSleepHours` | `8` | Target sleep duration under normal conditions. |
| `MinSleepHours` | `4` | Hard minimum — character will not try to sleep shorter than this. |
| `MaxSleepHours` | `12` | Hard maximum — even very fatigued characters cap here. |
| `SleepCooldownHours` | `16` | Minimum waking hours before sleep can be initiated again. |

#### Intent management

| Key | Default | Effect |
|---|---|---|
| `UseIntentManagement` | `true` | Enables cross-tick intent stabilization. Disable for fully reactive behavior. |
| `IntentSwitchMargin` | `10.0` | A new action must outscore the current intent by this margin to trigger a switch. Prevents flickering. |
| `IntentBaseBias` | `8.0` | Flat utility bonus given to the current intent each tick. |
| `IntentCommitmentBiasStep` | `1.0` | Additional bias added per tick the character stays committed. Long intentions become stickier. |
| `IntentTimeoutHours` | `2.0` | After this many in-game hours the intent expires even without completion. |
| `EmergencyIntentOverrideThreshold` | `75.0` | A need above this overrides intent regardless of commitment (biological emergency). |

#### Habit system

| Key | Default | Effect |
|---|---|---|
| `HabitLearningRate` | `0.08` | How quickly habit traces strengthen per reinforcement event. |
| `HabitDecayPerDay` | `0.015` | Daily decay of habit strength. Low = habits persist a long time. |
| `HabitMaxUtilityMultiplier` | `0.18` | Maximum proportional utility boost from a fully formed habit. |
| `HabitMaxFlatBias` | `4.0` | Maximum flat utility added on top of the multiplier. |
| `MaxHabitTraces` | `64` | Maximum stored habit traces per character. Oldest/weakest are pruned when exceeded. |

#### Social approach modulation

| Key | Default | Effect |
|---|---|---|
| `PrestigeReachOutBonusPerPoint` | `0.06` | ReachOut utility bonus per point of target's PerceivedPrestige above 50. |
| `DominanceAvoidancePenaltyPerPoint` | `0.08` | ReachOut utility penalty per point of target's PerceivedDominance above 70 when Closeness < 30. |

---

### `Characters:Sleep`

| Key | Default | Effect |
|---|---|---|
| `SleepPromptThreshold` | `70.0` | NeedRest value (0–100) above which the engine requests sleep. |
| `SleepGraceHours` | `4.0` | How long the engine waits after a prompt before applying decline penalties. |
| `MaxDeclineCount` | `3` | Maximum number of sleep declines before forced override. |
| `DeclinePenaltyStressPerHour` | `2.0` | Stress added per hour when sleep is declined past grace period. |
| `FallingDurationHours` | `0.25` | Time spent in the falling-asleep phase. |
| `LightDurationHours` | `0.75` | Duration of light sleep phase. |
| `DeepDurationHours` | `2.5` | Duration of deep sleep (primary restoration). |
| `RemDurationHours` | `1.5` | Duration of REM sleep (memory consolidation). |
| `AmbushBaseChancePerHour` | `0.03` | Base probability per hour of interruption during outdoor sleep. |
| `CompanionGuardModifier` | `0.4` | Multiplier on ambush chance when sleeping with a companion (`0.4` = 60% reduction). |
| `NightmareStressThreshold` | `70.0` | Stress above this increases nightmare probability. |
| `NightmareChanceHighStress` | `0.25` | Nightmare probability per session when stress exceeds threshold. |
| `NightmareChanceNormal` | `0.05` | Nightmare probability per session under normal stress. |
| `EmergencyNeedRestThreshold` | `90.0` | NeedRest above this triggers emergency sleep regardless of intent. |
| `EmergencyEnergyThreshold` | `5.0` | Energy below this also triggers emergency sleep. |
| `ThirstSleepBlockThreshold` | `80.0` | Thirst above this blocks sleep initiation (biological priority). |
| `HungerSleepBlockThreshold` | `80.0` | Hunger above this blocks sleep initiation. |

---

### `Characters:Interactions`

| Key | Default | Effect |
|---|---|---|
| `MisattributionRateBase` | `0.15` | Base probability that a character misattributes an interaction outcome (e.g., blaming the other person for environmental noise). |
| `NoiseAttributionAmplifier` | `0.40` | How much ambient noise amplifies the misattribution rate. High noise = harder to read social signals correctly. |

---

### `Characters:Relationships`

#### Decay and repair

| Key | Default | Effect |
|---|---|---|
| `DecayPerDay` | `1.5` | Global decay multiplier. Final decay per dimension = `DecayPerDay × DecayMultiplier[Dimension]`. |
| `RepairGain` | `6.0` | Relationship improvement after a successful repair attempt. |
| `RupturePenalty` | `8.0` | Relationship damage after a failed repair or transgression. |
| `ExpectedContactIntervalDays` | `14.0` | Contact gap above this accelerates decay (Navarro gap effect). |
| `NavarrGapMultiplier` | `3.0` | Decay acceleration factor when the expected contact interval is missed. |

#### Per-dimension decay multipliers

Final daily decay = `DecayPerDay × multiplier`:

| Key | Default | Dimension character |
|---|---|---|
| `DecayMultiplierTrust` | `0.06` | Extremely slow — hard to build, hard to lose passively |
| `DecayMultiplierRespect` | `0.04` | Slowest — reputational, very stable |
| `DecayMultiplierCloseness` | `0.35` | Moderate — needs regular meaningful contact |
| `DecayMultiplierLike` | `0.28` | Moderate |
| `DecayMultiplierComfort` | `0.80` | Fast — fades quickly without contact |
| `DecayMultiplierRomanticInterest` | `1.00` | Fast — needs active reinforcement |
| `DecayMultiplierSexualInterest` | `1.50` | Fastest — highly context-dependent |
| `DecayMultiplierFamiliarity` | `0.08` | Very slow — face recognition persists |
| `DecayMultiplierDominance` | `0.08` | Slow — status is stable |
| `DecayMultiplierPrestige` | `0.08` | Slow — reputation endures |

#### Mere exposure and familiarity

| Key | Default | Effect |
|---|---|---|
| `MereExposureMaxBoost` | `15.0` | Maximum total Attraction boost from repeated positive contact (logarithmic curve). |
| `MereExposureSaturation` | `20` | Number of interactions at which the mere-exposure boost saturates. |
| `FamiliarityDecayFloor` | `10.0` | Familiarity never decays below this — faces are remembered even after long absence. |
| `FamiliarityLikeDissonancePenalty` | `0.04` | Like decay accelerator when Familiarity is high but interactions have been neutral/negative. |

#### Transgression system

| Key | Default | Effect |
|---|---|---|
| `TransgressionDecayRatePerDay` | `0.04` | How quickly transgression residue fades per day. |
| `TransgressionMicroNegativeGain` | `3.0` | Transgression residue added per MicroNegative event. |
| `TransgressionRejectionGain` | `6.0` | Transgression residue added per rejection event. |

#### Attachment and communal exchange

| Key | Default | Effect |
|---|---|---|
| `ClosenessAvoidanceCap` | `40.0` | Maximum Closeness achievable with an avoidant-attachment character. |
| `RejectionAnxietyAmplifier` | `0.6` | Amplifies rejection effects on NeedBelonging for anxious-attachment characters. |
| `CommunalGrowthPerIntimateInteraction` | `1.5` | Closeness gain per intimate interaction (self-disclosure, validation). |

#### Attraction plasticity

| Key | Default | Effect |
|---|---|---|
| `AttractionPlasticityPerInteraction` | `0.25` | How much attraction preferences shift per positive/negative interaction. |
| `TonicSexualInterestThreshold` | `40.0` | SexualInterest below this triggers tonic desire modulation. |
| `TonicSexualInterestDecayFactor` | `0.30` | Additional decay rate below the tonic threshold. |
| `SexualInterestSeedFactor` | `0.50` | How much initial physical attraction seeds SexualInterest on first meeting. |

#### Dunbar tiers (time-budget model)

| Key | Default | Effect |
|---|---|---|
| `DunbarTier1Threshold` | `70.0` | Closeness above this = Tier 1 (inner circle). |
| `DunbarTier2Threshold` | `40.0` | Closeness above this = Tier 2 (good friends). |
| `DunbarTier1Capacity` | `5` | Maximum Tier 1 relationships before attention budget pressure kicks in. |
| `DunbarTier2Capacity` | `15` | Maximum Tier 2 relationships. |
| `AttentionBudgetPressurePerExcessTier1` | `0.15` | Decay multiplier increase per Tier 1 relationship over capacity. |
| `AttentionBudgetPressurePerExcessTier2` | `0.05` | Decay multiplier increase per Tier 2 relationship over capacity. |

#### Dominance and prestige

| Key | Default | Effect |
|---|---|---|
| `PrestigeGainPerPositiveAct` | `2.0` | Prestige increase from public helping or generous behavior. |
| `DominanceGainPerNegativeAct` | `3.0` | Dominance increase from assertive or aggressive acts. |
| `PrestigeGainPerSelfDisclosure` | `1.0` | Prestige increase from vulnerability and self-disclosure (warmth signal). |
| `DominanceGainPerContempt` | `10.0` | Large dominance jump from contemptuous behavior. |
| `PrestigeReachOutBonusPerPoint` | `0.06` | Utility bonus when approaching high-prestige targets. |
| `DominanceAvoidancePenaltyPerPoint` | `0.08` | Utility penalty for unfamiliar dominant targets (Closeness < 30). |

---

### `Characters:Memory`

| Key | Default | Effect |
|---|---|---|
| `BaseEncoding` | `0.5` | Base memory strength at encoding (0–1). |
| `SleepConsolidationBoost` | `0.12` | Strength increase applied to recent memories after REM sleep. |
| `ForgettingRate` | `0.06` | Strength reduction per day (Ebbinghaus curve). |
| `PruneThreshold` | `0.01` | Memories below this strength are permanently removed. |
| `ReinforcementBoost` | `0.15` | Strength boost when the same event is re-encoded (repetition effect). |
| `EmotionDecayMod` | `0.5` | Multiplier on emotional salience decay. Low = emotional charge of memories fades slowly. |
| `StressDistortionWeight` | `0.35` | How much high stress distorts memory at encoding. |
| `ReconsolidationDriftRate` | `0.04` | Rate at which recalled memories drift during reconsolidation (memory malleability). |
| `CognitiveBurdenThreshold` | `0.65` | Cognitive burden above this degrades memory retrieval quality. |
| `DirectWitnessConfidence` | `0.90` | Default confidence for memories formed by direct witness. |
| `GossipConfidence` | `0.35` | Default confidence for hearsay-based knowledge. |
| `KnowledgeConfidenceDecayPerDay` | `0.005` | Daily confidence decay for stored knowledge facts. |
| `KnowledgePruneThreshold` | `0.05` | Knowledge entries below this confidence are pruned. |

---

### `Characters:SemanticMemory`

| Key | Default | Effect |
|---|---|---|
| `LearningRate` | `0.18` | How quickly a belief strengthens per confirming evidence. |
| `ContradictionRate` | `0.08` | How quickly contradicting evidence weakens a belief. |
| `DecayPerDay` | `0.01` | Daily passive belief decay. |
| `StabilityGainPerEvidence` | `0.08` | Stability increases per confirming event. Stable beliefs resist contradiction. |
| `PatternWindowSize` | `6` | Number of recent episodes examined to detect a pattern. |
| `MinimumPatternSupport` | `2` | Minimum matching episodes in the window required to update a belief. |
| `ContradictionStabilityHit` | `0.05` | Stability reduction per contradicting event. |

#### Attachment modulation

| Key | Default | Effect |
|---|---|---|
| `AttachmentLearningBoostAnxious` | `1.30` | Anxious characters learn beliefs 30% faster (hyper-vigilant to social signals). |
| `AttachmentLearningDiscountAvoidant` | `0.75` | Avoidant characters form beliefs more slowly. |
| `AttachmentSafeDiscountAvoidant` | `0.45` | Avoidant characters are especially resistant to forming EmotionallySafe beliefs. |
| `AttachmentLearningBoostDisorganized` | `1.15` | Disorganized attachment — slightly elevated but inconsistent learning. |
| `AttachmentContradictionBoostAnxious` | `1.20` | Contradictions hit anxious characters harder (rumination amplifier). |
| `AttachmentContradictionBoostDisorganized` | `1.40` | Disorganized characters are most destabilized by contradictions. |

#### Navarro toxic relationship model

| Key | Default | Effect |
|---|---|---|
| `NavarroCriticalMultiple` | `8` | Minimum ratio of negative-to-positive episodes to trigger accelerated decay. |
| `NavarroDecayAccelerator` | `3.0` | Decay multiplier when the Navarro pattern is detected (rapid disillusionment). |

---

### `Characters:Lod`

Controls how often the Behavior engine runs per cognitive resolution level.

| Key | Default | Effect |
|---|---|---|
| `PlayerBehaviorDecisionStep` | `00:05:00` | Player character makes a behavior decision every 5 in-game minutes. |
| `NearbyBehaviorDecisionStep` | `00:15:00` | Nearby characters decide every 15 minutes. |
| `BackgroundBehaviorDecisionStep` | `01:00:00` | Background characters decide hourly. |

---

### `Characters:Fidelity`

Controls the detail level of memory, perception, and social processing per LOD tier.

**MemoryFidelityLevel:** `Full` / `Reduced` / `Minimal`  
**PerceptionFidelityLevel:** `Full` / `LocalOnly` / `Coarse`  
**SocialFidelityLevel:** `Full` / `Reduced` / `Minimal`

| Key | Default | Effect |
|---|---|---|
| `PlayerMemory` | `Full` | Player stores all episodic events. |
| `NearbyMemory` | `Full` | Nearby NPCs store full episodic detail (they may become relevant later). |
| `BackgroundMemory` | `Reduced` | Background NPCs store only meaningful events; routine actions are skipped. |
| `PlayerPerception` | `Full` | Player perceives all characters and world objects in full detail. |
| `NearbyPerception` | `LocalOnly` | Nearby NPCs perceive only their immediate location. |
| `BackgroundPerception` | `Coarse` | Background NPCs have coarse perception — no fine-grained social reading. |
| `PlayerSocial` | `Full` | Full social graph processing for the player. |
| `NearbySocial` | `Full` | Nearby NPCs run full social updates. |
| `BackgroundSocial` | `Reduced` | Background NPCs run minimal social updates — only large relationship events are processed. |

---

## Project Structure

```
GameEngineTools/
├── Characters/
│   ├── Core/               # OrchestratedHuman, HumanContext, EnginesSnapshot
│   ├── Engines/
│   │   ├── Behavior/       # DefaultBehaviorEngine, BehaviorHabitLearning
│   │   │   └── Sleep/      # DefaultSleepCoordinator, DefaultSleepSession
│   │   ├── Physiology/     # DefaultPhysiologyEngine, PhysiologyConfig
│   │   ├── Psychology/     # DefaultPsychologyEngine, PsychologyConfig
│   │   ├── Interactions/   # DefaultInteractionEngine, InteractionConfig
│   │   ├── Relationships/  # DefaultRelationshipsEngine, RelationshipsConfig
│   │   ├── Memory/         # DefaultMemoryEngine, MemoryCognition, MemoryWhatParser
│   │   └── SemanticMemory/ # DefaultSemanticMemoryEngine, SemanticTargeting
│   ├── Hosting/            # DI extensions, fidelity policies, LOD runtime
│   └── Traits/             # Personality, BigFive, AttachmentStyle, Chronotype
├── World/
│   ├── Location/           # DefaultLocationService, LocationDescriptor
│   └── Utils/Time/         # WDateTime, WTimeSpan
├── Logging/                # CoreLog — single EventId registry
└── Constants/              # FileSystemConstant

GameSandbox/
├── appsettings.Characters.Default.json   # baseline config (all keys documented above)
├── appsettings.Characters.json           # scene-specific overrides
└── Program.cs                            # simulation entry point

EngineTests/                              # MSTest unit tests
```


