// ContingencySearchEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Generates foraging movement candidates when a character needs food or water
    /// but no suitable objects are present at their current location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This engine is the behavioral bridge between object scarcity and locomotion.
    /// When <see cref="ObjectAffordanceGatingEngine"/> removes an <c>Eat</c> or <c>Drink</c>
    /// candidate (because the object is absent), this engine ensures the character does
    /// not simply idle — it generates a <c>MoveTo:Food</c> or <c>MoveTo:Drink</c> candidate
    /// that routes the character toward a location where the required object exists.
    /// </para>
    /// <para>
    /// <b>Utility calibration:</b><br/>
    /// Foraging utility is deliberately set slightly below the utility of actually
    /// eating or drinking (weights 1.0 and 0.9 vs 1.2 and 1.1) so that if food
    /// happens to be available after all, the primary action always wins.
    /// </para>
    /// <para>
    /// <b>No-op conditions:</b>
    /// <list type="bullet">
    ///   <item><see cref="BehaviorContext.AvailableObjects"/> is <c>null</c>
    ///   (no provider wired — tests, headless runs).</item>
    ///   <item>Need score is below <see cref="MinNeedToSearch"/> — character is not
    ///   hungry or thirsty enough to bother moving.</item>
    ///   <item>Required object IS already available at the current location — the
    ///   primary action (<c>Eat</c>/<c>Drink</c>) will survive gating and win.</item>
    /// </list>
    /// </para>
    /// </remarks>
    internal sealed class ContingencySearchEngine : IBehaviorNeedEngine
    {
        #region Constants

        /// <summary>
        /// Minimum need score [0..100] required before a foraging candidate is generated.
        /// Below this threshold the character is not hungry or thirsty enough to seek food.
        /// </summary>
        private const double MinNeedToSearch = 20.0;

        #endregion Constants

        #region IBehaviorNeedEngine

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            // Foraging is disabled when no object provider is wired.
            // null = provider absent (tests / headless) — not the same as "no objects here".
            if (context.AvailableObjects is null)
                return BehaviorNeedOutput.Empty;

            var candidates = new List<BehaviorCandidate>(capacity: 2);

            // ── Food foraging ─────────────────────────────────────────────────────────
            // Only generate MoveTo:Food when the character is hungry AND food is absent.
            // If food is present, Eat survives gating and no foraging candidate is needed.
            if (context.State.NeedFood >= MinNeedToSearch &&
                !HasCategory(context.AvailableObjects, WorldObjectCategory.Food))
            {
                candidates.Add(new BehaviorCandidate(
                    MoveToFood,

                    // Slightly lower weight than Eat (1.2) so actual eating always beats foraging
                    // when both are possible (e.g., food was just dropped in the location).
                    BehaviorMath.Util(context.State.NeedFood, 1.0),
                    WTimeSpan.FromMinutes(20),
                    BehaviorDomain.Physiological,
                    Tags: new[] { "EnvironmentMovement" }));
            }

            // ── Drink foraging ────────────────────────────────────────────────────────
            if (context.State.NeedWater >= MinNeedToSearch &&
                !HasCategory(context.AvailableObjects, WorldObjectCategory.Drink))
            {
                candidates.Add(new BehaviorCandidate(
                    MoveToDrink,

                    // Slightly lower weight than Drink (1.1).
                    BehaviorMath.Util(context.State.NeedWater, 0.9),
                    WTimeSpan.FromMinutes(20),
                    BehaviorDomain.Physiological,
                    Tags: new[] { "EnvironmentMovement" }));
            }

            return candidates.Count == 0
                ? BehaviorNeedOutput.Empty
                : new BehaviorNeedOutput(Array.Empty<BehaviorDrive>(), candidates);
        }

        #endregion IBehaviorNeedEngine

        #region Helpers

        /// <summary>
        /// Returns <c>true</c> when at least one object in the list belongs to
        /// the requested category. Iterates without LINQ — hot path (every tick).
        /// </summary>
        /// <param name="objects">Objects at the character's current location.</param>
        /// <param name="category">Category to check for.</param>
        private static bool HasCategory(IReadOnlyList<WorldObject> objects, WorldObjectCategory category)
        {
            foreach (var obj in objects)
            {
                if (obj.Category == category)
                    return true;
            }

            return false;
        }

        #endregion Helpers
    }
}
