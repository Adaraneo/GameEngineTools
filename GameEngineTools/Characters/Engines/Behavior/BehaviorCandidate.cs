// BehaviorCandidate.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using GameEngineTools.World.Utils.Time;

    internal sealed record BehaviorCandidate(string Name, double Utility, WTimeSpan Duration, BehaviorDomain Domain, IReadOnlyList<string>? Tags = null);
}
