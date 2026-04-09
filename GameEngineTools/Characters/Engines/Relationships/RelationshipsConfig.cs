// RelationshipsConfig.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    /// <summary>
    /// Configuration for <see cref="IRelationshipsEngine"/>.
    /// </summary>
    public sealed record RelationshipsConfig(
        double DecayPerDay = 1.5,
        double RepairGain = 6.0,
        double RupturePenalty = 8.0,
        double MereExposureMaxBoost = 15.0,
        int MereExposureSaturation = 20)
    {
        /// <summary>Parameterless constructor required by DI options binding.</summary>
        public RelationshipsConfig() : this(1.5, 6.0, 8.0, 15.0, 20) { }
    }
}
