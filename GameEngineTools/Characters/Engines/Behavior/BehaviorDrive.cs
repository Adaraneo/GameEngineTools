// BehaviorDrive.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    /// <summary>
    /// Abstract pressure signal emitted by a need engine before final action shaping.
    /// </summary>
    internal sealed record BehaviorDrive(
        string Name,
        double Intensity,
        BehaviorDomain Domain);
}
