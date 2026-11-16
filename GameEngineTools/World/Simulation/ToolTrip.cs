// ToolTrip.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Traits;

    /// <summary>How far a seeker will walk for a tool: what the body can afford, then how willing the person is.</summary>
    internal static class ToolTrip
    {
        // Hours one way a person of average traits, calm and fed, will walk for a tool (a design pick).
        private const double BaseOneWayHours = 3.0;

        // Willingness moves by up to half of the base with Conscientiousness (up) and Neuroticism (down), by up to 30 % with stress and by up to half with need pressure (design picks).
        private const double TraitSpan = 0.5;
        private const double StressSpan = 0.3;
        private const double NeedSpan = 0.5;

        // A failed search is repeated after this many hours, doubled for every failure in a row up to MaxDoublings (design picks).
        private const double RetryBaseHours = 6.0;
        private const int MaxDoublings = 3;

        private static readonly PhysiologyConfig BodyRates = new();
        private static readonly double EmergencyLevel = new BehaviorConfig().EmergencyIntentOverrideThreshold;

        /// <summary>The most hours one way the seeker would walk now: the lesser of what the body affords and what the person is willing to.</summary>
        public static double MaxOneWayHours(Personality personality, double stress, double hunger, double thirst)
        {
            // The body: the round trip must end before hunger or thirst reaches the emergency level at the awake rates.
            var hoursToEmergency = Math.Min(
                Math.Max(0.0, EmergencyLevel - hunger) / BodyRates.HungerDriftAwakePerHour,
                Math.Max(0.0, EmergencyLevel - thirst) / BodyRates.ThirstDriftAwakePerHour);
            var affordable = hoursToEmergency / 2.0;

            var traits = 1.0 + TraitSpan * (personality.BigFive.Conscientiousness - 0.5) - TraitSpan * (personality.BigFive.Neuroticism - 0.5);
            var calm = 1.0 - StressSpan * Math.Clamp(stress, 0.0, 100.0) / 100.0;
            var fed = 1.0 - NeedSpan * Math.Clamp(Math.Max(hunger, thirst), 0.0, 100.0) / 100.0;
            var willing = BaseOneWayHours * traits * calm * fed;

            return Math.Min(affordable, willing);
        }

        /// <summary>Hours until a seeker with this many failed searches in a row looks again.</summary>
        public static double RetryHours(int failuresInARow)
            => RetryBaseHours * Math.Pow(2.0, Math.Min(Math.Max(failuresInARow - 1, 0), MaxDoublings));
    }
}
