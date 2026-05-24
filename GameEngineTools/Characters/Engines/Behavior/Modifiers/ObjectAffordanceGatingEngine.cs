// ObjectAffordanceGatingEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System.Collections.Generic;
    using GameEngineTools.World.Objects;
    using static ActionNames;

    /// <summary>
    /// Constraint gate that removes or suppresses behavior candidates when their
    /// required world objects are not present in the character's current location.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs <b>after</b> all utility-scoring modifier engines and <b>before</b>
    /// intent management. Its job is not to score — it enforces hard physical
    /// constraints: you cannot eat if there is no food nearby.
    /// </para>
    /// <para>
    /// <b>Hard gate</b>: candidate is removed entirely (<c>RemoveAll</c>).
    /// Used for actions that are physically impossible without the object.
    /// </para>
    /// <para>
    /// <b>Soft gate</b>: candidate utility is zeroed but the candidate remains.
    /// Used when the character can still intend the action — relevant for
    /// intent stabilization (e.g. Sleep without Shelter: the character wants
    /// to sleep but cannot commit yet).
    /// </para>
    /// </remarks>
    internal sealed class ObjectAffordanceGatingEngine : IBehaviorModifierEngine
    {
        #region Action requirement map

        /// <summary>
        /// Object constraint for a single action.
        /// </summary>
        private sealed record ActionRequirement(
            /// <summary>
            /// Object categories that satisfy this requirement. Any single match is sufficient.
            /// </summary>
            IReadOnlyList<WorldObjectCategory> RequiredCategories,

            /// <summary>
            /// <c>true</c>  → candidate removed when requirement unmet (hard gate).<br/>
            /// <c>false</c> → candidate utility zeroed when requirement unmet (soft gate).
            /// </summary>
            bool IsHardGate);

        /// <summary>
        /// Static, declarative map of action names to their object requirements.
        /// Actions not listed here have no object constraint and are never gated.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, ActionRequirement> ActionRequirements =
            new Dictionary<string, ActionRequirement>
            {
                // ── Hard gates ── candidate removed when object absent ─────────────
                { Eat,      new([WorldObjectCategory.Food],      IsHardGate: true)  },
                { Drink,    new([WorldObjectCategory.Drink],     IsHardGate: true)  },
                { Work,     new([WorldObjectCategory.Tool],      IsHardGate: true)  },
                { Create,   new([WorldObjectCategory.Tool],      IsHardGate: true)  },
                { SelfCare, new([WorldObjectCategory.Furniture], IsHardGate: true)  },

                // ── Soft gate ── utility zeroed; SleepCoordinator owns session lifecycle
                { Sleep,    new([WorldObjectCategory.Shelter],   IsHardGate: false) },
            };

        #endregion Action requirement map

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            if (context.AvailableObjects is null)
                return;

            ApplyGates(candidates, context.AvailableObjects);
        }

        #endregion IBehaviorModifierEngine

        #region Gate application

        /// <summary>
        /// Iterates all declared requirements and gates candidates whose object
        /// constraint is not satisfied.
        /// </summary>
        /// <param name="candidates">Mutable candidate list to gate.</param>
        /// <param name="availableObjects">
        /// Objects at the character's current location, or <c>null</c> when no
        /// <see cref="IWorldObjectProvider"/> is wired (tests, headless runs).
        /// </param>
        private static void ApplyGates(
            List<BehaviorCandidate> candidates,
            IReadOnlyList<WorldObject>? availableObjects)
        {
            foreach (var (actionName, requirement) in ActionRequirements)
            {
                if (IsRequirementMet(requirement, availableObjects))
                    continue; // Object present — leave candidates untouched.

                if (requirement.IsHardGate)
                {
                    // Physical impossibility: the action cannot happen at all.
                    candidates.RemoveAll(c => c.Name == actionName);
                }
                else
                {
                    // Soft suppression: zero utility so the candidate loses arbitration
                    // but stays visible for intent tracking.
                    for (var i = 0; i < candidates.Count; i++)
                    {
                        if (candidates[i].Name == actionName)
                            candidates[i] = candidates[i] with { Utility = 0.0 };
                    }
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> when at least one available object satisfies the requirement.
        /// </summary>
        /// <param name="requirement">The requirement to check.</param>
        /// <param name="availableObjects">Objects to search. Already filtered by availability.</param>
        private static bool IsRequirementMet(
            ActionRequirement requirement,
            IReadOnlyList<WorldObject>? availableObjects)
        {
            if (availableObjects.Count == 0)
                return false;

            // Iterate without LINQ — the list is small and this is a hot path (every tick).
            foreach (var obj in availableObjects)
            {
                if (requirement.RequiredCategories.Contains(obj.Category))
                    return true;
            }

            return false;
        }

        #endregion Gate application
    }
}
