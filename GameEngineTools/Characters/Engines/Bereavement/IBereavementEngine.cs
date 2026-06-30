// IBereavementEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Owns a character's grief process: registers losses (<see cref="BereavementOnset"/>), assigns a
    /// grief trajectory, advances the Dual-Process-Model oscillation each tick, and emits
    /// <see cref="GriefPang"/> waves whose affect deltas Psychology consumes. An auxiliary engine ticked
    /// after Memory/SemanticMemory, mirroring the social-comparison and self-concept engines.
    /// </summary>
    public interface IBereavementEngine : IEngine<BereavementState, BereavementConfig>
    {
    }
}
