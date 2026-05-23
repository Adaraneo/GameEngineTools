// ActiveIntent.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Lightweight cross-tick tendency that biases compatible actions without introducing planning.
    /// </summary>
    public sealed record ActiveIntent(
        BehaviorIntentKind Kind,
        string? TargetAction,
        WDateTime CreatedAt,
        WDateTime UpdatedAt,
        double Strength,
        int Commitment,
        WDateTime? ExpiresAt);
}
