// DarkCoreModifier.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Modifiers
{
    using System;
    using System.Collections.Generic;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Modulates action utility based on the character's position on the general dark-core
    /// (D-factor) axis (Moshagen, Hilbig &amp; Zettler 2018, <i>Psychological Review</i> 125(5)).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Characters high in D are disposed to maximise self-interest at others' expense.
    /// This translates behaviourally as:
    /// <list type="bullet">
    ///   <item><b>Raised utility</b> for antagonistic actions (<c>Fight</c>) — D correlates
    ///         r≈.65–.67 with self-reported aggression (Moshagen et al. 2020).</item>
    ///   <item><b>Lowered utility</b> for prosocial actions (<c>ReachOut</c>, <c>InviteIntimacy</c>)
    ///         — D correlates r≈−.31 to −.37 with empathy (Moshagen et al. 2020).</item>
    /// </list>
    /// Both effects scale <b>monotonically</b> with <c>DarkCore</c>: a unit increase in the
    /// axis value produces a strictly larger antagonism boost and prosocial penalty.
    /// </para>
    /// <para>
    /// If the character's <see cref="GameEngineTools.Characters.Traits.DarkCoreProfile"/> is null
    /// (character generated before this feature), the modifier is a no-op — backward compatible.
    /// </para>
    /// </remarks>
    internal sealed class DarkCoreModifier : IBehaviorModifierEngine
    {
        #region IBehaviorModifierEngine

        /// <inheritdoc/>
        public void Modify(BehaviorContext context, List<BehaviorCandidate> candidates)
        {
            // Null DarkCore → character predates this feature; no-op.
            var darkCoreValue = context.HumanContext.Personality.DarkCore?.DarkCore;
            if (darkCoreValue is not { } d || d <= 0.0)
                return;

            var cfg = context.Config;

            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];

                if (IsAntagonistic(candidate.Name))
                {
                    // Raise utility of antagonistic actions proportionally to DarkCore.
                    var bonus = d * cfg.DarkCoreAntagonismUtilityWeight;
                    candidates[i] = candidate with { Utility = candidate.Utility + bonus };
                }
                else if (IsProsocial(candidate.Name))
                {
                    // Lower utility of prosocial actions proportionally to DarkCore.
                    var penalty = d * cfg.DarkCoreProsocialPenaltyWeight;
                    candidates[i] = candidate with { Utility = Math.Max(0.0, candidate.Utility - penalty) };
                }
            }
        }

        #endregion IBehaviorModifierEngine

        #region Classification helpers

        /// <summary>
        /// Returns <c>true</c> when the action is antagonistic — directly targeting or threatening
        /// others. Fight is the canonical example; Flee is excluded (it is self-protective, not
        /// other-directed aggression).
        /// </summary>
        private static bool IsAntagonistic(string actionName)
            => actionName == Fight;

        /// <summary>
        /// Returns <c>true</c> when the action is prosocial — voluntarily directed at others'
        /// wellbeing or connection. Dark-core characters are less intrinsically motivated to
        /// reach out or offer intimacy (D↔low empathy r≈−.31; Moshagen et al. 2020).
        /// </summary>
        private static bool IsProsocial(string actionName)
            => actionName == ReachOut || actionName == InviteIntimacy;

        #endregion Classification helpers
    }
}
