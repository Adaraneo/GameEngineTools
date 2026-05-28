// ActionSlotMask.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Bitmask representing which body/mind channels a single action occupies.
    /// </summary>
    /// <remarks>
    /// Two actions can run simultaneously only when their masks share no bits.
    /// Example — sitting on a bench (Posture) and eating (Hands | Mouth) have no overlap.
    /// </remarks>
    [Flags]
    public enum ActionSlotMask
    {
        /// <summary>No channel occupied. Used for passive / background actions.</summary>
        None = 0,

        /// <summary>
        /// Body posture channel — sitting, lying down, standing at a fixture.
        /// </summary>
        Posture = 1 << 0,

        /// <summary>
        /// Hands channel — carrying, eating, crafting, picking up objects.
        /// </summary>
        Hands = 1 << 1,

        /// <summary>
        /// Mouth channel — eating, drinking, speaking.
        /// </summary>
        Mouth = 1 << 2,

        /// <summary>
        /// Cognitive / mind channel — reading, deep planning, focused work.
        /// </summary>
        Mind = 1 << 3,
    }
}
