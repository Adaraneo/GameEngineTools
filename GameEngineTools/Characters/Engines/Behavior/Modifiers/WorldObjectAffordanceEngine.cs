// WorldObjectAffordanceEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.World.Objects;
    using static ActionNames;

    /// <summary>
    /// Modifier engine that nudges candidate utility based on physical world objects
    /// present in the character's current location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This engine is the behavioral bridge between the world object system
    /// (<see cref="IWorldObjectProvider"/>) and the behavior pipeline.
    /// It runs as the last modifier — after <see cref="EnvironmentalAffordanceEngine"/> —
    /// so surface-level utility adjustments are already applied before object affordances
    /// add their layer on top.
    /// </para>
    /// <para>
    /// <b>How it works:</b><br/>
    /// Each <see cref="WorldObject"/> in the location carries a list of
    /// <see cref="WorldObjectAffordance"/> records. Each affordance maps to one or more
    /// action candidates via <see cref="AffordanceCandidateMap"/>.
    /// The utility delta for a candidate is:
    /// <code>
    ///   delta = satisfaction × MaxDelta × (relevantNeed / 100)
    /// </code>
    /// For need-satisfying affordances (Hunger, Rest, Social…) the delta is scaled by
    /// how much the character currently needs that domain — hungry characters benefit
    /// more from food objects than well-fed ones.
    /// For ambient affordances (MoodBoost, Warmth) the delta is flat.
    /// For threat affordances (StressRaise) the delta is a negative multiplier.
    /// </para>
    /// <para>
    /// <b>Stacking and cap:</b><br/>
    /// Multiple objects can contribute to the same candidate. Total additive delta per
    /// candidate is capped at <see cref="MaxTotalDeltaPerCandidate"/> to prevent a
    /// fully-equipped room from overwhelming need-based competition.
    /// </para>
    /// <para>
    /// <b>No-op when absent:</b><br/>
    /// If <see cref="BehaviorContext.AvailableObjects"/> is <c>null</c> (provider not wired),
    /// this engine returns immediately with zero cost.
    /// </para>
    /// </remarks>
    internal sealed class WorldObjectAffordanceEngine : IBehaviorModifierEngine
    {
        #region Constants

        /// <summary>
        /// Hard cap on the total additive utility delta this engine can contribute
        /// to any single candidate per tick.
        /// Prevents a room full of food/rest items from making hunger/fatigue trivial.
        /// </summary>
        private const double MaxTotalDeltaPerCandidate = 20.0;

        /// <summary>
        /// Maximum utility added to a candidate by a single affordance at 100% satisfaction
        /// and maximum need. Per-type caps are defined in <see cref="AffordanceDeltaCap"/>.
        /// </summary>
        private static readonly IReadOnlyDictionary<AffordanceType, double> AffordanceDeltaCap
            = new Dictionary<AffordanceType, double>
            {
                { AffordanceType.Hunger,       15.0 },   // food objects: high impact when starving
                { AffordanceType.Thirst, 15.0 },
                { AffordanceType.Rest,         12.0 },   // rest objects: strong pull when fatigued
                { AffordanceType.Social,       10.0 },   // social objects: campfire, tavern table
                { AffordanceType.Work,          9.0 },   // work objects: desk, workbench
                { AffordanceType.Entertainment, 9.0 },   // entertainment: lute, game board
                { AffordanceType.Warmth,        8.0 },   // ambient warmth — reduces escape pressure
                { AffordanceType.MoodBoost,     6.0 },   // ambient pleasant objects — art, candle
                { AffordanceType.StressRaise,   0.0 },   // handled separately as penalty multiplier
            };

        /// <summary>
        /// Fraction of candidate utility removed per unit of StressRaise satisfaction.
        /// e.g. a hazard with StressRaise:0.6 removes 0.6 × 0.25 = 15% from all adjacent actions.
        /// </summary>
        private const double StressRaisePenaltyFactor = 0.25;

        #endregion Constants

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            // Fast exit when no world object data is available (tests, headless runs).
            if (context.AvailableObjects is not { Count: > 0 })
                return;

            // Build accumulated deltas per candidate name.
            // Key = candidate action name; Value = total additive utility delta from objects.
            var deltas = new Dictionary<string, double>(candidates.Count);

            // Separately track the highest stress-raise penalty found in the location.
            // We apply it as a multiplier to penalty-sensitive actions rather than stacking it.
            var maxStressRaise = 0.0;

            foreach (var obj in context.AvailableObjects)
            {
                foreach (var affordance in obj.Affordances)
                {
                    if (affordance.Type == AffordanceType.StressRaise)
                    {
                        // Take the worst hazard, not a sum — one fire is enough to intimidate.
                        maxStressRaise = Math.Max(maxStressRaise, affordance.Satisfaction);
                        continue;
                    }

                    // Resolve which candidates this affordance type targets.
                    var targets = AffordanceCandidateMap.TargetsFor(affordance.Type);
                    if (targets.Length == 0)
                        continue;

                    // Scale delta by how much the character currently needs this domain.
                    var needScale = ResolveNeedScale(context, affordance.Type);
                    var cap = AffordanceDeltaCap.GetValueOrDefault(affordance.Type, 0.0);
                    var rawDelta = affordance.Satisfaction * cap * needScale;

                    foreach (var actionName in targets)
                        deltas[actionName] = deltas.GetValueOrDefault(actionName, 0.0) + rawDelta;
                }
            }

            // Apply accumulated deltas to matching candidates, respecting the per-candidate cap.
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                // Additive bonus from positive affordances.
                if (deltas.TryGetValue(candidate.Name, out var delta) && delta > 0.0)
                {
                    var bounded = Math.Min(delta, MaxTotalDeltaPerCandidate);
                    candidates[i] = candidate with { Utility = candidate.Utility + bounded };
                    candidate = candidates[i]; // refresh ref for penalty below
                }

                // Multiplicative penalty from hazards / stress-raising objects.
                if (maxStressRaise > 0.0 && StressSensitiveActions.Contains(candidate.Name))
                {
                    var multiplier = 1.0 - maxStressRaise * StressRaisePenaltyFactor;
                    candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility * multiplier) };
                }
            }
        }

        #endregion IBehaviorModifierEngine

        #region Need scaling

        /// <summary>
        /// Returns a [0, 1] scale factor representing how much the character currently
        /// needs the domain this affordance type targets.
        /// </summary>
        /// <remarks>
        /// Ambient affordances (Warmth, MoodBoost) are not need-scaled — they apply
        /// a flat benefit regardless of character state.
        /// </remarks>
        private static double ResolveNeedScale(BehaviorContext context, AffordanceType type)
        {
            var state = context.State;

            return type switch
            {
                AffordanceType.Hunger => state.NeedFood / 100.0,
                AffordanceType.Thirst => state.NeedWater / 100.0,
                AffordanceType.Rest => state.NeedRest / 100.0,
                AffordanceType.Social => state.NeedBelonging / 100.0,
                AffordanceType.Work => state.NeedCompetence / 100.0,
                AffordanceType.Entertainment => state.NeedCompetence / 100.0,

                // Ambient affordances provide a flat benefit — independent of need level.
                AffordanceType.Warmth => 1.0,
                AffordanceType.MoodBoost => 1.0,

                _ => 0.0
            };
        }

        #endregion Need scaling

        #region Static data: stress-sensitive actions

        /// <summary>
        /// Actions whose utility is penalised when a stress-raising object is present.
        /// Biological regulation (Eat, Drink) is intentionally excluded — even stressed
        /// characters still eat when hungry.
        /// </summary>
        private static readonly HashSet<string> StressSensitiveActions = new()
        {
            ReachOut,
            Work,
            Create,
            InviteIntimacy
        };

        #endregion Static data: stress-sensitive actions
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  AffordanceCandidateMap  (static lookup table, internal to this file)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps each <see cref="AffordanceType"/> to the action candidate names it influences.
    /// Centralises the affordance → action translation so it can be unit-tested independently.
    /// </summary>
    internal static class AffordanceCandidateMap
    {
        #region Lookup table

        /// <summary>
        /// Lookup: AffordanceType → array of candidate action names to boost.
        /// Empty array means the type is handled by a different mechanism (e.g. StressRaise).
        /// </summary>
        private static readonly IReadOnlyDictionary<AffordanceType, string[]> Map
            = new Dictionary<AffordanceType, string[]>
            {
                // ── Physiological ─────────────────────────────────────────────
                { AffordanceType.Hunger,        new[] { Eat } },
                { AffordanceType.Thirst, new[] {Drink} },
                { AffordanceType.Rest,          new[] { MoveToRest, Idle } },

                // ── Social ────────────────────────────────────────────────────
                { AffordanceType.Social,        new[] { ReachOut } },

                // ── Productive ────────────────────────────────────────────────
                { AffordanceType.Work,          new[] { Work } },
                { AffordanceType.Entertainment, new[] { Create } },

                // ── Ambient (flat, not need-scaled) ───────────────────────────
                //
                // Warmth: reduces the escape drive — boosts staying put (MoveToSocial or Idle)
                // rather than boosting a need-driven action, because warmth itself is environmental.
                { AffordanceType.Warmth,        new[] { Idle, MoveToSocial } },

                // MoodBoost: small lift to social and creative actions.
                { AffordanceType.MoodBoost,     new[] { ReachOut, Create } },

                // StressRaise: handled separately as a penalty multiplier — no additive targets.
                { AffordanceType.StressRaise,   Array.Empty<string>() },
            };

        #endregion Lookup table

        #region Public API

        /// <summary>
        /// Returns the candidate action names that the given affordance type targets.
        /// Returns an empty array if the type has no additive targets.
        /// </summary>
        /// <param name="type">The affordance type to look up.</param>
        public static string[] TargetsFor(AffordanceType type)
            => Map.TryGetValue(type, out var targets) ? targets : Array.Empty<string>();

        #endregion Public API
    }
}
