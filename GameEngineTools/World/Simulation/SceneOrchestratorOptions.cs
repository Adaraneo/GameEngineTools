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
    }
}
