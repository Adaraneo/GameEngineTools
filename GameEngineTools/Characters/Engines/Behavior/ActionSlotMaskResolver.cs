// ActionSlotMaskResolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using static ActionNames;

    /// <summary>
    /// Maps action names to their required <see cref="ActionSlotMask"/>.
    /// Single source of truth — all engines that need slot requirements query this class.
    /// </summary>
    public static class ActionSlotMaskResolver
    {
        #region Lookup table

        /// <summary>
        /// Slot requirements for all named actions.
        /// Actions not in this table return <see cref="ActionSlotMask.None"/> (safe default).
        /// </summary>
        private static readonly IReadOnlyDictionary<string, ActionSlotMask> Table =
            new Dictionary<string, ActionSlotMask>(StringComparer.OrdinalIgnoreCase)
            {
                // ── Physiological ──────────────────────────────────────────────
                [Eat]              = ActionSlotMask.Hands | ActionSlotMask.Mouth,
                [Drink]            = ActionSlotMask.Hands | ActionSlotMask.Mouth,
                [SelfCare]         = ActionSlotMask.Hands,
                [ActionNames.Sleep] = ActionSlotMask.Posture | ActionSlotMask.Hands | ActionSlotMask.Mind,

                // ── Work ───────────────────────────────────────────────────────
                [Work]             = ActionSlotMask.Hands | ActionSlotMask.Mind,
                [Create]           = ActionSlotMask.Hands | ActionSlotMask.Mind,

                // ── Social ─────────────────────────────────────────────────────
                [ReachOut]         = ActionSlotMask.Mouth,
                [InviteIntimacy]   = ActionSlotMask.Mouth,

                // ── Movement ───────────────────────────────────────────────────
                [MoveToSocial]     = ActionSlotMask.Posture,
                [MoveToPrivate]    = ActionSlotMask.Posture,
                [MoveToWork]       = ActionSlotMask.Posture,
                [MoveToRest]       = ActionSlotMask.Posture,
                [MoveToPublic]     = ActionSlotMask.Posture,
                [MoveToFood]       = ActionSlotMask.Posture,
                [MoveToDrink]      = ActionSlotMask.Posture,

                // ── Affordance-driven object interactions ─────────────────────
                [UseObjectForRest]   = ActionSlotMask.Posture,
                [UseObjectForWork]   = ActionSlotMask.Hands | ActionSlotMask.Mind,
                [UseObjectForFun]    = ActionSlotMask.Hands | ActionSlotMask.Mind,
                [UseObjectForWarmth] = ActionSlotMask.None,
                [UseObjectForMood]   = ActionSlotMask.None,
                [GatherAtObject]     = ActionSlotMask.None,

                // ── Idle ───────────────────────────────────────────────────────
                [Idle]             = ActionSlotMask.None,
            };

        #endregion Lookup table

        #region Public API

        /// <summary>
        /// Returns the <see cref="ActionSlotMask"/> for the given action name.
        /// </summary>
        /// <param name="actionName">The action name to look up.</param>
        /// <param name="objectInteraction">
        /// Optional object interaction payload. When the action is
        /// <see cref="ActionNames.InteractWithObject"/>, the mask is resolved
        /// from the interaction kind rather than from the table.
        /// </param>
        /// <returns>
        /// The required slot mask. Returns <see cref="ActionSlotMask.None"/> for
        /// unrecognised action names — safe default that imposes no constraint.
        /// </returns>
        public static ActionSlotMask Get(string actionName, ObjectInteractionData? objectInteraction = null)
        {
            // Legacy InteractWithObject: resolve from interaction kind (Take/UseInPlace/Drop).
            // Affordance-driven names (UseObject:*) already have correct entries in the table.
            if (string.Equals(actionName, InteractWithObject, StringComparison.OrdinalIgnoreCase)
                && objectInteraction is not null)
            {
                return objectInteraction.Kind switch
                {
                    ObjectInteractionKind.Take       => ActionSlotMask.Hands,
                    ObjectInteractionKind.UseInPlace => ActionSlotMask.Posture,
                    ObjectInteractionKind.Drop       => ActionSlotMask.Hands,
                    _                                => ActionSlotMask.None,
                };
            }

            return Table.TryGetValue(actionName, out var mask) ? mask : ActionSlotMask.None;
        }

        #endregion Public API
    }
}
