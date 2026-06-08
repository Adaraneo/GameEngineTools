// IObjectInteractionPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;

    /// <summary>Decides whether a character is permitted to interact with a world object (ownership, social rules).</summary>
    public interface IObjectInteractionPolicy
    {
        /// <summary>Evaluates whether an object interaction is permitted.</summary>
        /// <param name="actor">The character attempting the interaction.</param>
        /// <param name="target">The target world object.</param>
        /// <param name="kind">The kind of interaction requested.</param>
        /// <param name="location">The location the interaction occurs in.</param>
        /// <returns>A permission result, including a refusal reason when denied.</returns>
        ObjectInteractionPermission Evaluate(
            IHumanContext actor,
            WorldObject target,
            ObjectInteractionKind kind,
            LocationDescriptor location);
    }

    /// <summary>Result of an <see cref="IObjectInteractionPolicy"/> evaluation.</summary>
    /// <param name="IsAllowed">Whether the interaction is permitted.</param>
    /// <param name="RefusalReason">Human-readable reason when denied, otherwise <c>null</c>.</param>
    /// <param name="IsSocial">Whether the interaction is socially observed/relevant.</param>
    public sealed record ObjectInteractionPermission(
        bool IsAllowed,
        string? RefusalReason = null,
        bool IsSocial = false);
}
