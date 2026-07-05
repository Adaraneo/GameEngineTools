// ContingencySearchEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Generates foraging movement candidates when a character needs food or water
    /// but no suitable objects are present at their current location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This engine is the behavioral bridge between object scarcity and locomotion.
    /// When <see cref="GameEngineTools.Characters.Engines.Behavior.Modifiers.ObjectAffordanceGatingEngine"/> removes an <c>Eat</c> or <c>Drink</c>
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

        // Food-economy Tier 1 provisioning weights, ordered by diet-breadth return rate
        // (optimal-foraging: prefer the option that satiates soonest for the least labor).
        // Eat (world Food, PhysiologicalNeedsEngine) = 1.2 is the top of this ladder.
        private const double EatStoredWeight = 1.15; // eat from hand: immediate, no labor
        private const double ProcessWeight   = 1.05; // cook held inputs: some labor, then edible
        private const double ProduceWeight   = 0.90; // harvest raw material: furthest from a meal

        #endregion Constants

        #region Construction

        private readonly SpoilageConfig _spoilage;

        /// <summary>Creates the bridge; spoilage rates default to <see cref="SpoilageConfig.Default"/>.</summary>
        public ContingencySearchEngine(SpoilageConfig? spoilage = null)
            => _spoilage = spoilage ?? SpoilageConfig.Default;

        #endregion Construction

        #region IBehaviorNeedEngine

        /// <inheritdoc/>
        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            // Foraging is disabled when no object provider is wired.
            // null = provider absent (tests / headless) — not the same as "no objects here".
            if (context.AvailableObjects is null)
                return BehaviorNeedOutput.Empty;

            var candidates = new List<BehaviorCandidate>(capacity: 2);

            // ── Food provisioning ladder (Tier 1) + foraging fallback ───────────────────
            // When hungry, offer every actionable way to reach a meal; arbitration picks the
            // best by utility. Options are gated to what is actually possible here-and-now.
            if (context.State.NeedFood >= MinNeedToSearch)
            {
                var need = context.State.NeedFood;

                // 1) Eat from hand — a fresh, edible item is already carried.
                if (context.HeldObjects is { } held && HasFreshFood(held, context.Now))
                {
                    candidates.Add(new BehaviorCandidate(
                        EatStored,
                        BehaviorMath.Util(need, EatStoredWeight),
                        WTimeSpan.FromMinutes(15),
                        BehaviorDomain.Physiological,
                        Tags: new[] { "FoodEconomy" }));
                }

                // 2) Process held/co-located inputs into food at a co-located processing site.
                if (TryFindSatisfiableRecipe(context.AvailableObjects, context.HeldObjects, out var recipe))
                {
                    candidates.Add(new BehaviorCandidate(
                        Process,
                        BehaviorMath.Util(need, ProcessWeight),
                        WTimeSpan.FromMinutes(recipe!.DurationMinutes),
                        BehaviorDomain.Physiological,
                        Tags: new[] { "FoodEconomy" }));
                }

                // 3) Harvest raw material at a co-located raw-production site.
                if (HasRawProductionSite(context.AvailableObjects))
                {
                    candidates.Add(new BehaviorCandidate(
                        Produce,
                        BehaviorMath.Util(need, ProduceWeight),
                        WTimeSpan.FromMinutes(30),
                        BehaviorDomain.Physiological,
                        Tags: new[] { "FoodEconomy" }));
                }

                // 4) Foraging fallback — no ready Food object here → move toward one.
                if (!HasCategory(context.AvailableObjects, WorldObjectCategory.Food))
                {
                    candidates.Add(new BehaviorCandidate(
                        MoveToFood,

                        // Slightly lower weight than Eat (1.2) so actual eating always beats foraging
                        // when both are possible (e.g., food was just dropped in the location).
                        BehaviorMath.Util(need, 1.0),
                        WTimeSpan.FromMinutes(20),
                        BehaviorDomain.Physiological,
                        Tags: new[] { "EnvironmentMovement" }));
                }
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

        /// <summary>True when the character carries at least one still-edible (unspoiled) food item.</summary>
        private bool HasFreshFood(IReadOnlyList<WorldObject> held, WDateTime now)
        {
            foreach (var obj in held)
            {
                if (obj.Category == WorldObjectCategory.Food && Spoilage.Freshness(obj, now, _spoilage) > 0.0)
                    return true;
            }
            return false;
        }

        /// <summary>True when a co-located object is a raw-production site (Production affordance, no recipe).</summary>
        private static bool HasRawProductionSite(IReadOnlyList<WorldObject> availableObjects)
        {
            foreach (var obj in availableObjects)
            {
                if (obj.Affordances.Any(a => a.Type == AffordanceType.Production)
                    && RecipeRegistry.FindByOutput(obj.ItemKind) is null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds a co-located processing site (Production affordance whose output has a recipe) for
        /// which the character's held plus co-located items satisfy every input. Returns the recipe.
        /// </summary>
        private static bool TryFindSatisfiableRecipe(
            IReadOnlyList<WorldObject> availableObjects,
            IReadOnlyList<WorldObject>? heldObjects,
            out Recipe? recipe)
        {
            foreach (var site in availableObjects)
            {
                if (!site.Affordances.Any(a => a.Type == AffordanceType.Production))
                    continue;
                var candidate = RecipeRegistry.FindByOutput(site.ItemKind);
                if (candidate is null)
                    continue; // raw site, not a processing site
                if (CanSatisfy(candidate, availableObjects, heldObjects))
                {
                    recipe = candidate;
                    return true;
                }
            }
            recipe = null;
            return false;
        }

        /// <summary>
        /// All-or-nothing check that held + co-located items cover every <see cref="RecipeInput"/>,
        /// counting each item toward at most one input line (mirrors <c>ProductionService</c>).
        /// </summary>
        private static bool CanSatisfy(
            Recipe recipe,
            IReadOnlyList<WorldObject> availableObjects,
            IReadOnlyList<WorldObject>? heldObjects)
        {
            var pool = (heldObjects ?? Array.Empty<WorldObject>()).Concat(availableObjects);
            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in recipe.Inputs)
            {
                var matched = pool
                    .Where(o => o.ItemKind == input.Kind && !usedIds.Contains(o.Id))
                    .Take(input.Quantity)
                    .ToList();
                if (matched.Count < input.Quantity)
                    return false;
                foreach (var o in matched) usedIds.Add(o.Id);
            }
            return true;
        }

        #endregion Helpers
    }
}
