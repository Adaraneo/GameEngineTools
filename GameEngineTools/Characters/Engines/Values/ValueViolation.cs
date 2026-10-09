// ValueViolation.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Values
{
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Decides whether a committed action violates the character's current values badly enough to feel guilt.
    /// </summary>
    internal static class ValueViolation
    {
        /// <summary>
        /// Congruence below this threshold counts as a guilt-worthy violation. Congruence is a dot product normalised by 10, so the
        /// achievable range is only about [-0.12 .. +0.12] (see <see cref="ValuesConfig.PositiveCongruenceThreshold"/>); -0.01 therefore
        /// marks a moderate violation. The earlier documentation of -0.30 was on the theoretical +-1 scale and could never be reached.
        /// </summary>
        internal const double GuiltThreshold = -0.01;

        /// <summary>
        /// Checks an action against the profile; only actions with a negative loading can be guilt-worthy
        /// (Tangney &amp; Dearing 2002: guilt follows one's own action against one's moral standards).
        /// </summary>
        /// <param name="actionName">The committed action.</param>
        /// <param name="values">The character's current (drifting) values.</param>
        /// <param name="congruence">The computed congruence when a violation is detected.</param>
        /// <param name="dominantViolatedValue">The value most strongly violated, when a violation is detected.</param>
        /// <returns><c>true</c> when the action violates the values below <see cref="GuiltThreshold"/>.</returns>
        internal static bool TryDetect(string actionName, ValuesProfile values, out double congruence, out string dominantViolatedValue)
        {
            congruence = 0.0;
            dominantViolatedValue = "Unknown";

            var loading = ActionValueLoadings.Get(actionName);
            if (loading == ValueLoadVector.Zero) return false;

            congruence = loading.Congruence(values);
            if (congruence >= GuiltThreshold || !loading.HasNegativeLoading) return false;

            dominantViolatedValue = FindDominantViolatedValue(loading, values);
            return true;
        }

        /// <summary>Returns the name of the value most strongly violated by the action, weighted by the character's profile.</summary>
        internal static string FindDominantViolatedValue(ValueLoadVector loading, ValuesProfile profile)
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
    }
}
