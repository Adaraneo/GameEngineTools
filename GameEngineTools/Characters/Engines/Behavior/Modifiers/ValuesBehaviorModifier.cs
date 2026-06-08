// ValuesBehaviorModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Adjusts candidate utility based on alignment between the action's value loadings
    /// and the character's <see cref="ValuesProfile"/>.
    /// </summary>
    /// <remarks>
    /// Architecture: pure <see cref="IBehaviorModifierEngine"/> — reads snapshot, modifies
    /// utility, and emits <see cref="ValueCongruenceViolated"/> via the outbox when a
    /// guilt-threshold violation is detected.
    /// <para>
    /// Value-action gap gating (Mieth, Buchner &amp; Bell 2021): under high cognitive load or
    /// stress, the value-congruence contribution to utility is attenuated — simulating
    /// bounded rationality under pressure.
    /// </para>
    /// <para>
    /// Reference: Cranefield, Winikoff, Dignum &amp; Dignum (2017, IJCAI-17) — value-based
    /// plan selection in BDI agents via utility weighting.
    /// </para>
    /// </remarks>
    internal sealed class ValuesBehaviorModifier : IBehaviorModifierEngine
    {
        #region Constants

        /// <summary>
        /// Weight λ for the value-congruence contribution to utility.
        /// Calibrated so a perfectly aligned action gains ~+5 utility points
        /// and a strongly conflicting action loses ~−5 points at typical need levels.
        /// Tunable via playtest (recommended range 0.25–0.55).
        /// </summary>
        private const double UtilityLambda = 0.40;

        /// <summary>
        /// Congruence below this threshold emits <see cref="ValueCongruenceViolated"/>
        /// and triggers a Guilt spike in <c>DefaultPsychologyEngine</c>.
        /// Calibrated to normalised dot-product / 10: typical violation magnitude is 0.01–0.05,
        /// so -0.01 captures moderate value-violating actions with matching character profiles.
        /// </summary>
        private const double GuiltThreshold = -0.01;

        /// <summary>
        /// Cognitive load above this value attenuates the value-utility contribution
        /// (value-action gap; Mieth et al. 2021).
        /// </summary>
        private const double CogLoadAttenuationThreshold = 55.0;

        #endregion Constants

        #region Private fields

        private readonly ILogger? _log;

        #endregion Private fields

        #region Construction

        /// <summary>
        /// Initialises the modifier. Logger is optional — omit in unit tests.
        /// </summary>
        public ValuesBehaviorModifier(ILogger? log = null) => _log = log;

        #endregion Construction

        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            // No values profile → character was created before this sprint; skip silently.
            // Read the drifting Current profile (R4 drift) — morality is keyed to who the
            // character has become, not the BigFive-seeded Baseline.
            var values = context.HumanContext.Snapshot.Values?.Current;
            if (values is null) return;

            var psych = context.HumanContext.Snapshot.Psychology;

            // Value-action gap: under high cognitive load or stress, values exert less influence.
            // Stress attenuation: linear falloff above 70 (Mieth et al. 2021).
            var stressAttenuation = psych.Stress > 70.0
                ? Math.Clamp(1.0 - (psych.Stress - 70.0) / 30.0, 0.20, 1.0)
                : 1.0;

            // Cognitive load attenuation: linear falloff above threshold.
            var cogLoadAttenuation = psych.CognitiveLoad > CogLoadAttenuationThreshold
                ? Math.Clamp(1.0 - (psych.CognitiveLoad - CogLoadAttenuationThreshold) / 45.0, 0.20, 1.0)
                : 1.0;

            var attenuation = stressAttenuation * cogLoadAttenuation;

            for (var i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                var loading = ActionValueLoadings.Get(c.Name);
                if (loading == ValueLoadVector.Zero) continue;

                var congruence = loading.Congruence(values);

                // Apply utility delta: positive congruence boosts; negative reduces.
                // Scale by attenuated λ so the effect is proportional to how freely
                // the character can deliberate (Mieth et al. 2021 value-action gap).
                var utilityDelta = congruence * UtilityLambda * 100.0 * attenuation;
                candidates[i] = c with { Utility = Math.Max(0.0, c.Utility + utilityDelta) };

                // Log significant shifts only (threshold 1.5 utility units to avoid log spam).
                if (_log is not null && Math.Abs(utilityDelta) > 1.5)
                {
                    using (_log.BeginCharacterScope(context.HumanContext.Id.Value, nameof(ValuesBehaviorModifier)))
                    {
                        _log.ValueCongruenceViolationDetected(
                            context.HumanContext.Id.Value.ToString(),
                            c.Name,
                            congruence,
                            utilityDelta);
                    }
                }

                // Emit guilt event if congruence is below the guilt threshold.
                // Only for the self-transcendence violation path (Benevolence, Universalism).
                // See Tangney & Dearing (2002): guilt = own action against one's moral standards.
                if (congruence < GuiltThreshold && loading.HasNegativeLoading)
                {
                    var dominantViolated = FindDominantViolatedValue(loading, values);
                    context.Outbox.Add(new ValueCongruenceViolated(
                        context.Now,
                        context.HumanContext.Id,
                        c.Name,
                        congruence,
                        dominantViolated));
                }
            }
        }

        #endregion IBehaviorModifierEngine

        #region Private helpers

        /// <summary>
        /// Returns the name of the value most strongly violated by this action,
        /// weighted by the character's value profile.
        /// </summary>
        private static string FindDominantViolatedValue(ValueLoadVector loading, ValuesProfile profile)
        {
            var worst = 0.0;
            var worstName = "Unknown";

            Check(loading.Benevolence * profile.Benevolence, nameof(ValuesProfile.Benevolence));
            Check(loading.Universalism * profile.Universalism, nameof(ValuesProfile.Universalism));
            Check(loading.SelfDirection * profile.SelfDirection, nameof(ValuesProfile.SelfDirection));
            Check(loading.Stimulation * profile.Stimulation, nameof(ValuesProfile.Stimulation));
            Check(loading.Hedonism * profile.Hedonism, nameof(ValuesProfile.Hedonism));
            Check(loading.Achievement * profile.Achievement, nameof(ValuesProfile.Achievement));
            Check(loading.Power * profile.Power, nameof(ValuesProfile.Power));
            Check(loading.Security * profile.Security, nameof(ValuesProfile.Security));
            Check(loading.Conformity * profile.Conformity, nameof(ValuesProfile.Conformity));
            Check(loading.Tradition * profile.Tradition, nameof(ValuesProfile.Tradition));

            return worstName;

            void Check(double weightedLoad, string name)
            {
                if (weightedLoad < worst)
                {
                    worst = weightedLoad;
                    worstName = name;
                }
            }
        }

        #endregion Private helpers
    }
}
