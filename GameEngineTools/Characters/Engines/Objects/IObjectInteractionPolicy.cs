// IObjectInteractionPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;

    public interface IObjectInteractionPolicy
    {
        ObjectInteractionPermission Evaluate(
            IHumanContext actor,
            WorldObject target,
            ObjectInteractionKind kind,
            LocationDescriptor location);
    }

    public sealed record ObjectInteractionPermission(
        bool IsAllowed,
        string? RefusalReason = null,
        bool IsSocial = false);
}
