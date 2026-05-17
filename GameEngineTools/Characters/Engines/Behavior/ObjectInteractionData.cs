// ObjectInteractionData.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Payload attached to a <see cref="BehaviorCandidate"/> when the selected action is
    /// <see cref="ActionNames.InteractWithObject"/>. Carries enough information for
    /// <c>DefaultObjectInteractionEngine</c> to resolve and execute the interaction.
    /// </summary>
    public sealed record ObjectInteractionData(
        string ObjectId,
        string LocationId,
        ObjectInteractionKind Kind);

    /// <summary>
    /// What the character intends to do with the target world object.
    /// </summary>
    public enum ObjectInteractionKind
    {
        /// <summary>Pick up the object and carry it.</summary>
        Take,

        /// <summary>Use the object at its current location (no pickup).</summary>
        UseInPlace,

        /// <summary>Drop a held object at the current location.</summary>
        Drop
    }
}
