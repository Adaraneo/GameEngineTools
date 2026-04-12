// CharacterFidelityConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting
{
    /// <summary>
    /// Runtime fidelity defaults loaded from appsettings.Characters.json.
    /// </summary>
    public sealed record CharacterFidelityConfig(
        MemoryFidelityLevel PlayerMemory = MemoryFidelityLevel.Full,
        MemoryFidelityLevel NearbyMemory = MemoryFidelityLevel.Full,
        MemoryFidelityLevel BackgroundMemory = MemoryFidelityLevel.Reduced,
        PerceptionFidelityLevel PlayerPerception = PerceptionFidelityLevel.Full,
        PerceptionFidelityLevel NearbyPerception = PerceptionFidelityLevel.LocalOnly,
        PerceptionFidelityLevel BackgroundPerception = PerceptionFidelityLevel.Coarse,
        SocialFidelityLevel PlayerSocial = SocialFidelityLevel.Full,
        SocialFidelityLevel NearbySocial = SocialFidelityLevel.Full,
        SocialFidelityLevel BackgroundSocial = SocialFidelityLevel.Reduced)
    {
        public CharacterFidelityConfig()
            : this(
                MemoryFidelityLevel.Full,
                MemoryFidelityLevel.Full,
                MemoryFidelityLevel.Reduced,
                PerceptionFidelityLevel.Full,
                PerceptionFidelityLevel.LocalOnly,
                PerceptionFidelityLevel.Coarse,
                SocialFidelityLevel.Full,
                SocialFidelityLevel.Full,
                SocialFidelityLevel.Reduced)
        { }
    }
}
