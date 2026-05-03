// SexualResponsiveness.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Traits
{
    /// <summary>
    /// Dual Control Model (DCM) sexual responsiveness profile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The DCM (Bancroft &amp; Janssen 2000; Janssen &amp; Bancroft 2007) is the best-replicated model
    /// in sexology. It models sexual response as the balance between excitation and two
    /// independent inhibitory systems — not as a single "libido" scalar.
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <b>SES</b> — Sexual Excitation System [0–1].
    ///     Sensitivity to sexually relevant stimuli.
    ///     High SES: NeedIntimacy builds quickly from attractive cues.
    ///     Low SES: slow or absent spontaneous arousal.
    ///   </item>
    ///   <item>
    ///     <b>SIS1</b> — Sexual Inhibition System 1 — Performance / failure-based [0–1].
    ///     Suppresses excitation when failure, pain, or anxiety is anticipated.
    ///     High SIS1: NeedIntimacy falls under stress (HPA activation).
    ///   </item>
    ///   <item>
    ///     <b>SIS2</b> — Sexual Inhibition System 2 — Threat / consequence-based [0–1].
    ///     Suppresses excitation in unsafe or socially risky contexts (crowding, strangers).
    ///     High SIS2: NeedIntimacy falls in high-crowding environments.
    ///   </item>
    /// </list>
    /// <para>
    /// All three are independent: high SES + high SIS1 models a person who is strongly
    /// responsive but inhibited under stress; low SES + low SIS describes low overall drive.
    /// </para>
    /// <para>
    /// Add to <see cref="Personality"/> as a nullable field; <c>null</c> = use population
    /// averages (SES=0.5, SIS1=0.5, SIS2=0.5) — no change to existing behaviour.
    /// </para>
    /// </remarks>
    public sealed record SexualResponsiveness(
        /// <summary>Sexual Excitation System [0–1]. Default population average ≈ 0.50.</summary>
        double SES,

        /// <summary>Sexual Inhibition System 1 — performance/failure-based [0–1]. Default ≈ 0.50.</summary>
        double SIS1,

        /// <summary>Sexual Inhibition System 2 — threat/context-based [0–1]. Default ≈ 0.50.</summary>
        double SIS2)
    {
        /// <summary>Population-average baseline — no deviation from default behaviour.</summary>
        public static SexualResponsiveness Default => new(0.5, 0.5, 0.5);

        /// <summary>High excitation, low inhibition — strong spontaneous desire, low context-sensitivity.</summary>
        public static SexualResponsiveness HighExcitation => new(0.85, 0.25, 0.25);

        /// <summary>Low excitation, high inhibition — responsive but rarely spontaneous.</summary>
        public static SexualResponsiveness HighlyInhibited => new(0.25, 0.80, 0.70);
    }
}
