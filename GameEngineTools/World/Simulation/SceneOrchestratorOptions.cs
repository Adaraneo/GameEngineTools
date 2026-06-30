// SceneOrchestratorOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    /// <summary>
    /// Tunable scalar parameters for <see cref="DefaultSceneOrchestrator"/>.
    /// All defaults reproduce the orchestrator's historical hard-coded behavior, so the
    /// orchestrator behaves identically when no configuration is supplied. Bind from
    /// appsettings (section "SceneOrchestrator") or construct directly in tests.
    /// </summary>
    public sealed record SceneOrchestratorOptions
    {
        /// <summary>
        /// Softmax temperature for reach-out target selection in <c>DynamicReachOutRouting</c>
        /// (passed to <c>SemanticTargeting.ChooseTargetWeighted</c>). Lower → greedier (closer
        /// to argmax); higher → more exploratory. Default 0.25 reproduces the legacy value.
        /// </summary>
        public double ReachOutExplorationTemperature { get; init; } = 0.25;

        /// <summary>
        /// Probability [0-1] that a witnessed creative action emits a <c>MicroPositive</c>
        /// in <c>OrganicMicroPositives</c>. Default 0.30 reproduces the legacy gate.
        /// </summary>
        public double OrganicMicroPositiveChance { get; init; } = 0.30;

        /// <summary>
        /// Minimum confidence [0-1] required for an <c>ObjectLocationFact</c> to be used when
        /// routing a foraging character in <c>RouteMoveTo</c>. Facts below this are treated as
        /// forgotten for navigation. Default 0.15 reproduces the legacy const.
        /// </summary>
        public double MinMemoryConfidence { get; init; } = 0.15;

        /// <summary>
        /// When <c>true</c>, <c>MoveTo:*</c> actions no longer relocate a character instantly.
        /// Instead the orchestrator holds the character in transit at their origin for the travel
        /// duration (<see cref="World.Movement.TravelDurationComputer"/> over the destination
        /// distance at the character's current movement speed), then places them on arrival.
        /// While in transit further <c>MoveTo</c> actions are ignored — the character is committed
        /// to the current trip. Default <c>false</c> reproduces the legacy instant-move behavior.
        /// </summary>
        public bool EnableTravelTime { get; init; } = false;

        /// <summary>
        /// Travel distance in metres assumed when a chosen destination is not an adjacent
        /// graph edge (a cross-locale fallback reached via <c>FindBestMoveTarget</c> /
        /// <c>FindLocationWithCategory</c> rather than a direct <see cref="World.Objects.WorldConnection"/>).
        /// Only consulted when <see cref="EnableTravelTime"/> is <c>true</c>. Default 1200 m.
        /// </summary>
        public double FallbackTravelDistanceMeters { get; init; } = 1200.0;

        /// <summary>
        /// Minimum edge <c>Closeness</c> (0–100) a non-kin survivor must hold toward the deceased to be
        /// routed a <c>BereavementOnset</c> when a death occurs. Kin (any <c>KinRole</c>) always mourn
        /// regardless of this threshold. Default 35 — acquaintances below this do not grieve.
        /// </summary>
        public double BereavementClosenessThreshold { get; init; } = 35.0;
    }
}
