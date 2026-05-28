// ActionValueLoadings.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using GameEngineTools.Characters.Traits;

    /// <summary>
    /// Catalog of action → value-load vectors for all named GET actions.
    /// </summary>
    /// <remarks>
    /// A positive loading means the action expresses or reinforces that value.
    /// A negative loading means the action violates that value and may trigger guilt.
    /// <para>
    /// Source: Bardi &amp; Schwartz (2003) "Strength and Structure of Relations Among Values and Behaviors";
    /// Sagiv et al. (2011) values and cooperation; Parks-Leduc et al. (2015) value-behaviour predictions.
    /// Values with no loading for a given action are implicitly 0.
    /// </para>
    /// <para>
    /// The <b>value-action gap</b> (Mieth, Buchner &amp; Bell 2021): under high cognitive load or stress,
    /// the utility contribution of this catalog should be attenuated by a stress-suppression factor.
    /// See <see cref="Modifiers.ValuesBehaviorModifier"/> for the implementation.
    /// </para>
    /// </remarks>
    public static class ActionValueLoadings
    {
        #region Loading table

        /// <summary>
        /// Returns the value-load vector for the given action name.
        /// Returns an empty record (all zeros) when the action has no value loadings defined.
        /// </summary>
        /// <param name="actionName">Action name from <see cref="ActionNames"/>.</param>
        public static ValueLoadVector Get(string actionName)
            => Table.TryGetValue(actionName, out var v) ? v : ValueLoadVector.Zero;

        /// <summary>
        /// Returns <c>true</c> when the action has at least one negative loading that
        /// could trigger a guilt event.
        /// </summary>
        public static bool HasGuiltRisk(string actionName)
            => Table.TryGetValue(actionName, out var v) && v.HasNegativeLoading;

        // ── Internal table ────────────────────────────────────────────────────

        private static readonly IReadOnlyDictionary<string, ValueLoadVector> Table =
            new ReadOnlyDictionary<string, ValueLoadVector>(BuildTable());

        private static Dictionary<string, ValueLoadVector> BuildTable() => new()
        {
            // ── Competence domain ─────────────────────────────────────────────
            [ActionNames.Work] = new ValueLoadVector(
                Achievement: +0.55, Conformity: +0.30, Security: +0.20,
                Hedonism: -0.20, Stimulation: -0.15),

            [ActionNames.Create] = new ValueLoadVector(
                SelfDirection: +0.65, Stimulation: +0.35, Achievement: +0.20,
                Conformity: -0.15, Tradition: -0.10),

            // ── Social domain ─────────────────────────────────────────────────
            [ActionNames.ReachOut] = new ValueLoadVector(
                Benevolence: +0.50, Universalism: +0.20),

            [ActionNames.InviteIntimacy] = new ValueLoadVector(
                Hedonism: +0.45, Benevolence: +0.30, Stimulation: +0.20,
                Conformity: -0.25, Tradition: -0.20),

            // ── Physiological domain ──────────────────────────────────────────
            [ActionNames.Eat] = new ValueLoadVector(
                Hedonism: +0.30, Security: +0.20),

            [ActionNames.SelfCare] = new ValueLoadVector(
                Hedonism: +0.40, Security: +0.30,
                Achievement: -0.15),

            [ActionNames.Sleep] = new ValueLoadVector(
                Security: +0.20, Hedonism: +0.15,
                Achievement: -0.10),

            // ── Exploration / movement ────────────────────────────────────────
            [ActionNames.MoveToPublic] = new ValueLoadVector(
                Stimulation: +0.30, Universalism: +0.15),

            [ActionNames.MoveToSocial] = new ValueLoadVector(
                Benevolence: +0.30, Stimulation: +0.20),

            [ActionNames.MoveToWork] = new ValueLoadVector(
                Achievement: +0.30, Conformity: +0.20, Security: +0.15),

            [ActionNames.MoveToPrivate] = new ValueLoadVector(
                SelfDirection: +0.25, Security: +0.20),

            // ── Idle ──────────────────────────────────────────────────────────
            [ActionNames.Idle] = new ValueLoadVector(
                Hedonism: +0.10, Achievement: -0.20, Conformity: -0.10),
        };

        #endregion Loading table
    }

    /// <summary>
    /// Signed value-load vector for one action across all 10 Schwartz dimensions.
    /// </summary>
    /// <remarks>
    /// Guilt channel: when the actor's <see cref="ValuesProfile"/> has high weight on
    /// Benevolence or Universalism AND the action's loading on those dimensions is negative,
    /// <see cref="Modifiers.ValuesBehaviorModifier"/> emits a <c>ValueCongruenceViolated</c> event.
    /// </remarks>
    public sealed record ValueLoadVector(
        double Benevolence   = 0,
        double Universalism  = 0,
        double SelfDirection = 0,
        double Stimulation   = 0,
        double Hedonism      = 0,
        double Achievement   = 0,
        double Power         = 0,
        double Security      = 0,
        double Conformity    = 0,
        double Tradition     = 0)
    {
        /// <summary>Zero-loading vector (no value impact).</summary>
        public static ValueLoadVector Zero { get; } = new();

        /// <summary>
        /// Returns <c>true</c> when any dimension has a negative loading
        /// (action violates at least one value).
        /// </summary>
        public bool HasNegativeLoading =>
            Benevolence < 0 || Universalism < 0 || SelfDirection < 0 || Stimulation < 0 ||
            Hedonism < 0    || Achievement < 0  || Power < 0         || Security < 0    ||
            Conformity < 0  || Tradition < 0;

        /// <summary>
        /// Computes the congruence of this action with the given values profile.
        /// Returns a value in [−1..+1]: positive = action aligns with values;
        /// negative = action conflicts with values.
        /// </summary>
        /// <remarks>
        /// Formula: normalised dot-product of action loadings × profile weights.
        /// Guilt threshold is triggered when congruence &lt; −0.30
        /// (see <see cref="Modifiers.ValuesBehaviorModifier"/>).
        /// </remarks>
        public double Congruence(ValuesProfile profile)
        {
            var dot =
                Benevolence   * profile.Benevolence   +
                Universalism  * profile.Universalism  +
                SelfDirection * profile.SelfDirection +
                Stimulation   * profile.Stimulation   +
                Hedonism      * profile.Hedonism       +
                Achievement   * profile.Achievement   +
                Power         * profile.Power         +
                Security      * profile.Security      +
                Conformity    * profile.Conformity    +
                Tradition     * profile.Tradition;

            // Normalise to [−1..+1] by dividing by the number of dimensions.
            return Math.Clamp(dot / 10.0, -1.0, 1.0);
        }
    }
}
