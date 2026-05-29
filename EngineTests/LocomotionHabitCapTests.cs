// EngineTests/LocomotionHabitCapTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Verifies that locomotion actions (<c>MoveTo:*</c>) are capped by
    /// <see cref="BehaviorConfig.LocomotionHabitMultiplierCap"/> and
    /// <see cref="BehaviorConfig.LocomotionHabitFlatBiasCap"/>, while terminal
    /// actions like <c>ReachOut</c> still use the full general ceiling.
    /// </summary>
    [TestClass]
    public class LocomotionHabitCapTests : TestBase
    {
        #region Constants

        // Strong habit — simulates a character who has chosen this action many times.
        private const double MaxHabitStrength = 1.0;

        // Base utility before any habit modification.
        private const double BaseUtility = 95.0;

        #endregion Constants

        // ── Test 1 ──────────────────────────────────────────────────────────
        // MoveTo:Social must not exceed the locomotion cap even at max habit strength.

        /// <summary>
        /// MoveTo:Social with a saturated habit trace must stay within the locomotion
        /// ceiling, not the general habit ceiling.
        /// </summary>
        [TestMethod]
        public void Modify_MoveToSocial_MaxHabit_StaysBelowLocomotionCeiling()
        {
            // Arrange
            var config = new BehaviorConfig(
                HabitMaxUtilityMultiplier: 0.18,
                HabitMaxFlatBias: 4.0,
                LocomotionHabitMultiplierCap: 0.06,
                LocomotionHabitFlatBiasCap: 1.0);

            var context = ContextWithTrace(MoveToSocial, MaxHabitStrength, config);
            var candidates = new List<BehaviorCandidate>
            {
                new(MoveToSocial, BaseUtility, WTimeSpan.FromMinutes(20), BehaviorDomain.Social)
            };

            // Act
            new LearnedHabitEngine().Modify(context, candidates);

            // Assert
            // Max allowed: 95 * (1 + 0.06) + 1.0 = 101.7
            var locomotionCeiling = BaseUtility * (1.0 + config.LocomotionHabitMultiplierCap)
                                    + config.LocomotionHabitFlatBiasCap;

            Assert.IsTrue(
                candidates[0].Utility <= locomotionCeiling + 0.001,
                $"MoveTo:Social must not exceed locomotion ceiling ({locomotionCeiling:F2}). " +
                $"Actual: {candidates[0].Utility:F2}");
        }

        // ── Test 2 ──────────────────────────────────────────────────────────
        // ReachOut must still use the full general ceiling.

        /// <summary>
        /// ReachOut with a saturated habit trace must be allowed to reach the
        /// full general ceiling, not be limited by the locomotion cap.
        /// </summary>
        [TestMethod]
        public void Modify_ReachOut_MaxHabit_ReachesFullGeneralCeiling()
        {
            // Arrange
            var config = new BehaviorConfig(
                HabitMaxUtilityMultiplier: 0.18,
                HabitMaxFlatBias: 4.0,
                LocomotionHabitMultiplierCap: 0.06,
                LocomotionHabitFlatBiasCap: 1.0);

            var context = ContextWithTrace(ReachOut, MaxHabitStrength, config);
            var candidates = new List<BehaviorCandidate>
            {
                new(ReachOut, BaseUtility, WTimeSpan.FromHours(1), BehaviorDomain.Social)
            };

            // Act
            new LearnedHabitEngine().Modify(context, candidates);

            // Assert — ReachOut must be able to beat the locomotion ceiling
            var locomotionCeiling = BaseUtility * (1.0 + config.LocomotionHabitMultiplierCap)
                                    + config.LocomotionHabitFlatBiasCap;

            Assert.IsTrue(
                candidates[0].Utility > locomotionCeiling,
                $"ReachOut with full habit should exceed the locomotion ceiling ({locomotionCeiling:F2}). " +
                $"Actual: {candidates[0].Utility:F2}");
        }

        // ── Test 3 ──────────────────────────────────────────────────────────
        // With cap in place, ReachOut can beat a fully-habituated MoveTo:Social.

        /// <summary>
        /// When both candidates have equal base utility and max habit strength,
        /// ReachOut must win after the locomotion cap is applied.
        /// This is the core scenario the cap was designed to fix.
        /// </summary>
        [TestMethod]
        public void Modify_EqualBaseUtility_ReachOutBeatsMaxHabitMoveToSocial()
        {
            // Arrange
            var config = new BehaviorConfig(
                HabitMaxUtilityMultiplier: 0.18,
                HabitMaxFlatBias: 4.0,
                LocomotionHabitMultiplierCap: 0.06,
                LocomotionHabitFlatBiasCap: 1.0);

            var context = ContextWithTraces(
                new[] { (MoveToSocial, MaxHabitStrength), (ReachOut, MaxHabitStrength) },
                config);

            var candidates = new List<BehaviorCandidate>
            {
                new(MoveToSocial, BaseUtility, WTimeSpan.FromMinutes(20), BehaviorDomain.Social),
                new(ReachOut,     BaseUtility, WTimeSpan.FromHours(1),    BehaviorDomain.Social),
            };

            // Act
            new LearnedHabitEngine().Modify(context, candidates);

            var moveToUtility = candidates.Find(c => c.Name == MoveToSocial)!.Utility;
            var reachOutUtility = candidates.Find(c => c.Name == ReachOut)!.Utility;

            // Assert
            Assert.IsTrue(
                reachOutUtility > moveToUtility,
                $"ReachOut ({reachOutUtility:F2}) must beat MoveTo:Social ({moveToUtility:F2}) " +
                $"when both have max habit and equal base utility.");
        }

        // ── Test 4 ──────────────────────────────────────────────────────────
        // The locomotion cap applies to ALL MoveTo:* variants.

        /// <summary>
        /// The locomotion ceiling must apply uniformly to all <c>MoveTo:*</c> action names,
        /// not only <c>MoveTo:Social</c>.
        /// </summary>
        [TestMethod]
        public void Modify_AllMoveToVariants_AreCappedAtLocomotionCeiling()
        {
            // Arrange
            var config = new BehaviorConfig(
                HabitMaxUtilityMultiplier: 0.18,
                HabitMaxFlatBias: 4.0,
                LocomotionHabitMultiplierCap: 0.06,
                LocomotionHabitFlatBiasCap: 1.0);

            var locomotionCeiling = BaseUtility * (1.0 + config.LocomotionHabitMultiplierCap)
                                    + config.LocomotionHabitFlatBiasCap;

            var moveActions = new[] { MoveToSocial, MoveToPrivate, MoveToWork, MoveToRest, MoveToPublic };

            foreach (var action in moveActions)
            {
                var context = ContextWithTrace(action, MaxHabitStrength, config);
                var candidates = new List<BehaviorCandidate>
                {
                    new(action, BaseUtility, WTimeSpan.FromMinutes(20), BehaviorDomain.Social)
                };

                // Act
                new LearnedHabitEngine().Modify(context, candidates);

                // Assert
                Assert.IsTrue(
                    candidates[0].Utility <= locomotionCeiling + 0.001,
                    $"'{action}' must be capped at locomotion ceiling ({locomotionCeiling:F2}). " +
                    $"Actual: {candidates[0].Utility:F2}");
            }
        }

        // ── Test 5 ──────────────────────────────────────────────────────────
        // Default config keeps locomotion cap below the general ceiling.

        /// <summary>
        /// Default <see cref="BehaviorConfig"/> must have a locomotion cap that is
        /// strictly less than the general habit ceiling, so the fix is active
        /// even without explicit appsettings configuration.
        /// </summary>
        [TestMethod]
        public void BehaviorConfig_Default_LocomotionCapBelowGeneralCeiling()
        {
            // Arrange
            var config = new BehaviorConfig();

            // Assert
            Assert.IsTrue(
                config.LocomotionHabitMultiplierCap < config.HabitMaxUtilityMultiplier,
                $"LocomotionHabitMultiplierCap ({config.LocomotionHabitMultiplierCap}) " +
                $"must be less than HabitMaxUtilityMultiplier ({config.HabitMaxUtilityMultiplier}).");

            Assert.IsTrue(
                config.LocomotionHabitFlatBiasCap < config.HabitMaxFlatBias,
                $"LocomotionHabitFlatBiasCap ({config.LocomotionHabitFlatBiasCap}) " +
                $"must be less than HabitMaxFlatBias ({config.HabitMaxFlatBias}).");
        }

        #region Test helpers

        /// <summary>
        /// Builds a <see cref="BehaviorContext"/> with a single habit trace for the given action.
        /// Uses noon on a Social surface so cue and surface matching produce a meaningful bias.
        /// Config is injected via <c>with { Config = ... }</c> after factory construction.
        /// </summary>
        private static BehaviorContext ContextWithTrace(
            string actionName,
            double strength,
            BehaviorConfig config)
            => ContextWithTraces(new[] { (actionName, strength) }, config);

        /// <summary>
        /// Builds a <see cref="BehaviorContext"/> with multiple habit traces.
        /// Each trace uses <see cref="SurfaceKind.Social"/> and <see cref="HabitTimeBand.Day"/>
        /// to match the noon Social surface of the context.
        /// </summary>
        private static BehaviorContext ContextWithTraces(
            IEnumerable<(string ActionName, double Strength)> actions,
            BehaviorConfig config)
        {
            var traceDict = new Dictionary<string, BehaviorHabitTrace>(StringComparer.Ordinal);
            var i = 0;
            foreach (var (actionName, strength) in actions)
            {
                // Key is arbitrary — ComputeCandidateBias filters traces by ActionName, not key.
                traceDict[$"trace-{i++}"] = new BehaviorHabitTrace(
                    ActionName: actionName,
                    SurfaceKind: SurfaceKind.Social,
                    TimeBand: HabitTimeBand.Day,
                    CueKind: HabitCueKind.SocialNeed,
                    Strength: strength,
                    AdaptiveReinforcement: 0.5,
                    CopingReinforcement: 0.1,
                    RepetitionCount: 50,
                    LastUpdatedAt: new WDateTime(0),
                    Tendency: HabitTendency.Neutral);
            }

            var stateWithTraces = new BehaviorState(
                NeedRest: 10, NeedFood: 5, NeedWater: 5,
                NeedBelonging: 75, NeedCompetence: 50, NeedIntimacy: 30,
                CurrentPlan: null,
                HabitTraces: traceDict);

            // Noon → Day time band. Social surface → surface matching applies.
            var now = new WDateTime(WTimeSpan.FromHours(12).Ticks);

            var context = BehaviorComponentTestFactory.Context(
                now: now,
                state: stateWithTraces,
                surfaceKind: SurfaceKind.Social);

            // Factory hardcodes new BehaviorConfig() — override with our custom config.
            return context with { Config = config };
        }

        #endregion Test helpers
    }
}