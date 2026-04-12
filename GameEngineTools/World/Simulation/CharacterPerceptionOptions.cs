// CharacterPerceptionOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    /// <summary>
    /// Configures perception-based filtering used by simulation orchestration.
    /// </summary>
    public sealed record CharacterPerceptionOptions
    {
        /// <summary>
        /// Maximum number of locally perceived targets for LocalOnly fidelity.
        /// </summary>
        public int MaxLocalOnlyTargets { get; init; } = 4;

        /// <summary>
        /// Maximum number of perceived targets for Coarse fidelity.
        /// </summary>
        public int MaxCoarseTargets { get; init; } = 1;

        /// <summary>
        /// Noise threshold above which LocalOnly perception collapses.
        /// </summary>
        public double LocalOnlyNoiseThreshold { get; init; } = 0.85;

        /// <summary>
        /// Crowding threshold above which LocalOnly perception collapses.
        /// </summary>
        public double LocalOnlyCrowdingThreshold { get; init; } = 0.90;

        /// <summary>
        /// Noise threshold above which Coarse perception collapses.
        /// </summary>
        public double CoarseNoiseThreshold { get; init; } = 0.60;

        /// <summary>
        /// Crowding threshold above which Coarse perception collapses.
        /// </summary>
        public double CoarseCrowdingThreshold { get; init; } = 0.70;
    }
}
