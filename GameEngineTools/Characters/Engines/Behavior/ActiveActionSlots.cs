// ActiveActionSlots.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Tracks which action channels are currently occupied for a single character.
    /// </summary>
    /// <remarks>
    /// Not serialised into EnginesSnapshot — volatile runtime state that resets on scene reload.
    /// Not thread-safe. Characters are always ticked sequentially.
    /// </remarks>
    internal sealed class ActiveActionSlots
    {
        #region Nested types

        /// <summary>A single occupied slot entry.</summary>
        private sealed record SlotEntry(string ActionName, WDateTime ExpiresAt);

        #endregion Nested types

        #region Private state

        /// <summary>Per-slot active entry. Key = individual ActionSlotMask bit.</summary>
        private readonly Dictionary<ActionSlotMask, SlotEntry> _entries = new();

        #endregion Private state

        #region Public API

        /// <summary>The combined mask of all currently occupied slots.</summary>
        public ActionSlotMask OccupiedMask
        {
            get
            {
                var result = ActionSlotMask.None;
                foreach (var key in _entries.Keys)
                    result |= key;
                return result;
            }
        }

        /// <summary>
        /// Returns <see langword="true"/> when all channels in <paramref name="mask"/> are free.
        /// </summary>
        public bool IsFree(ActionSlotMask mask) =>
            mask == ActionSlotMask.None || (OccupiedMask & mask) == ActionSlotMask.None;

        /// <summary>
        /// Acquires the slots declared by <paramref name="mask"/>.
        /// If a slot is already occupied by a different action, it is replaced —
        /// the newly committed action wins (BehaviorEngine already ran arbitration).
        /// </summary>
        public void AcquireOrReplace(string actionName, ActionSlotMask mask, WDateTime now, WTimeSpan duration)
        {
            if (mask == ActionSlotMask.None)
                return;

            var expiry = now + duration;
            foreach (var bit in EnumerateBits(mask))
                _entries[bit] = new SlotEntry(actionName, expiry);
        }

        /// <summary>
        /// Releases all slots whose expiry time has passed.
        /// Call at the start of each tick before processing new committed actions.
        /// </summary>
        public void ExpireAll(WDateTime now)
        {
            var expired = _entries
                .Where(kv => kv.Value.ExpiresAt <= now)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in expired)
                _entries.Remove(key);
        }

        /// <summary>
        /// Forcibly releases the slot(s) held by <paramref name="actionName"/>.
        /// Used when an action is interrupted or cancelled.
        /// </summary>
        public void Release(string actionName)
        {
            var toRemove = _entries
                .Where(kv => string.Equals(kv.Value.ActionName, actionName, StringComparison.Ordinal))
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
                _entries.Remove(key);
        }

        #endregion Public API

        #region Private helpers

        /// <summary>
        /// Enumerates each individual set bit in <paramref name="mask"/> as a
        /// separate <see cref="ActionSlotMask"/> value.
        /// </summary>
        private static IEnumerable<ActionSlotMask> EnumerateBits(ActionSlotMask mask)
        {
            foreach (ActionSlotMask bit in Enum.GetValues<ActionSlotMask>())
            {
                if (bit != ActionSlotMask.None && (mask & bit) == bit)
                    yield return bit;
            }
        }

        #endregion Private helpers
    }
}
