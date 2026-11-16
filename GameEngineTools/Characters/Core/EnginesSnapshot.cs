// EnginesSnapshot.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using Characters.Engines.Behavior;
    using Characters.Engines.Goals;
    using Characters.Engines.Interactions;
    using Characters.Engines.Physiology;
    using Characters.Engines.Psychology;
    using Characters.Engines.Relationships;
    using Characters.Engines.Schedule;
    using Characters.Engines.SemanticMemory;
    using Characters.Traits;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Core.Astro;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Unified read-only snapshot of every engine's state plus ambient environment context.
    /// Used for Phase-A event delivery and persistence.
    /// </summary>
    /// <param name="Physiology">Physiology engine state.</param>
    /// <param name="Psychology">Psychology engine state.</param>
    /// <param name="Behavior">Behavior engine state.</param>
    /// <param name="InteractionSurface">Current interaction surface.</param>
    /// <param name="Relationships">Relationship graph state.</param>
    /// <param name="Memory">Episodic memory index.</param>
    /// <param name="SemanticMemory">Semantic (belief) memory state, or <c>null</c>.</param>
    public sealed record EnginesSnapshot(
        PhysiologyState Physiology,
        PsychologyState Psychology,
        BehaviorState Behavior,
        InteractionSurface InteractionSurface,
        RelationshipState Relationships,
        MemoryIndex Memory,
        SemanticMemoryState? SemanticMemory = null,
        /// <summary>
        /// Ambient temperature at the character's location (°C). Set by the simulation layer per tick
        /// from <c>LocationDescriptor</c>. Default 20 °C = neutral indoors.
        /// Read by the Psychology engine (heat → aggression, mild cold → affiliation).
        /// </summary>
        double AmbientTemperature = 20.0,
        /// <summary>
        /// Altitude at the character's location (m). Set by the simulation layer per tick.
        /// &gt;2000 m → hypoxie (Energy↓, CogLoad↑). &gt;4000 m → AMS (Pain↑).
        /// Default 0 = sea level.
        /// </summary>
        double AltitudeMeters = 0.0,
        /// <summary>
        /// Astronomical context for the current tick — irradiance, day length, sunrise/sunset,
        /// season and ambient temperature. Set by <see cref="GameEngineTools.World.Simulation.SimulationScene"/> via
        /// <see cref="IHuman.SetAmbientContext"/> before each character's tick.
        /// <c>null</c> if the astronomical logic is not configured.
        /// </summary>
        CelestialContext? Celestial = null,
        /// <summary>
        /// Persistent long-term goals for this character.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// </summary>
        GoalState? Goals = null,
        /// <summary>
        /// Daily schedule state for this character.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// </summary>
        DailyScheduleState? Schedule = null,

        /// <summary>
        /// Schwartz Basic Human Values state for this character: the drifting <c>Current</c>
        /// profile plus the immutable <c>Baseline</c> seeded from BigFive at creation.
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Read by <see cref="GameEngineTools.Characters.Engines.Behavior.Modifiers.ValuesBehaviorModifier"/>;
        /// evolved by <see cref="GameEngineTools.Characters.Engines.Values.DefaultValuesEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Values.ValuesState? Values = null,

        /// <summary>
        /// The character's self-concept (perceived Big Five, ideal subset, self-esteem, discrepancy).
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.SelfConcept.DefaultSelfConceptEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.SelfConcept.SelfConcept? SelfConcept = null,

        /// <summary>
        /// The character's RIASEC interest state (drifting Current + immutable Baseline).
        /// <c>null</c> for characters created before this field existed (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.Interests.DefaultInterestEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Interests.InterestState? Interests = null,

        /// <summary>
        /// The character's social comparison state (throttle timestamp for the reflective
        /// comparison cadence). <c>null</c> for characters created before this field existed
        /// or when the engine is not wired (backward compatibility).
        /// Evolved by <see cref="GameEngineTools.Characters.Engines.Social.DefaultSocialComparisonEngine"/>.
        /// </summary>
        GameEngineTools.Characters.Engines.Social.SocialComparisonState? SocialComparison = null,

        /// <summary>
        /// Derived SDT basic-need appraisal (Competence/Relatedness/Autonomy satisfaction &amp;
        /// frustration), computed each tick from goals/relationships/regulatory-focus.
        /// <c>null</c> for characters created before this field existed or when the appraisal engine
        /// is not wired; the engine initialises it to <see cref="GameEngineTools.Characters.Engines.Behavior.NeedAppraisal.NeedAppraisalState.Empty"/>
        /// on its first tick (backward compatibility).
        /// </summary>
        GameEngineTools.Characters.Engines.Behavior.NeedAppraisal.NeedAppraisalState? NeedAppraisal = null,

        /// <summary>
        /// The character's emergent two-axis social status (Dominance/Prestige), as conferred by the
        /// surrounding network. <c>null</c> until the scene's <c>StatusLedger</c> injects it (or when the
        /// social-hierarchy subsystem is not wired). Pushed in by
        /// <see cref="GameEngineTools.Characters.Core.IHuman.SetSocietalStatus"/>, not by any engine.
        /// Read by Psychology (status×stability stress).
        /// </summary>
        GameEngineTools.Characters.Engines.Status.SocietalStatus? SocietalStatus = null,

        /// <summary>
        /// Local hierarchy stability [0,1] (1 = stable) at the character's locale, injected alongside
        /// <see cref="SocietalStatus"/>. Default 1.0 (stable / unknown) preserves legacy behaviour.
        /// </summary>
        double HierarchyStability = 1.0,

        /// <summary>
        /// Per-deceased bereavement state (loss records, grief trajectories, DPM oscillation phase).
        /// <c>null</c> for characters created before this field existed or when the bereavement engine is
        /// not wired (backward compatibility). Evolved by
        /// <see cref="GameEngineTools.Characters.Engines.Bereavement.DefaultBereavementEngine"/>;
        /// read by Physiology (widowhood hazard).
        /// </summary>
        GameEngineTools.Characters.Engines.Bereavement.BereavementState? Bereavement = null,

        /// <summary>
        /// Per-character economic state: current wealth in the single abstract currency unit ("coin").
        /// <c>null</c> for characters created before this field existed or when the economy engine is
        /// not wired (backward compatibility — treated as <c>Wealth = 0</c>). Evolved by
        /// <see cref="GameEngineTools.Characters.Engines.Economy.DefaultEconomyEngine"/>. Food-economy Tier 2.
        /// </summary>
        GameEngineTools.Characters.Traits.EconomyState? Economy = null);
}
