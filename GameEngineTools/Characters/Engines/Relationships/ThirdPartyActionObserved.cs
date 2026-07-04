// ThirdPartyActionObserved.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    using Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Published when a character witnesses another person's action toward a third party.
    /// The observer updates their relationship edge toward the actor based on what they saw.
    /// </summary>
    /// <remarks>
    /// Gossip and reputation effects are among the most consistent findings in social psychology
    /// (Feinberg et al. 2014, <i>Psychological Science</i>; Wu, Balliet &amp; Van Lange 2016):
    /// third-party observation of behaviour shapes reputation and deters anti-social acts.
    /// Used in prior art: City of Gangsters, Talk of the Town, Crusader Kings 3.
    /// </remarks>
    public sealed record ThirdPartyActionObserved(
        WDateTime OccurredAt,

        /// <summary>The character who witnessed the action.</summary>
        HumanId Observer,

        /// <summary>The character who performed the action.</summary>
        HumanId Actor,

        /// <summary>The character the action was directed at.</summary>
        HumanId Target,

        /// <summary>Valence of the observed action: positive = kind/helpful, negative = hostile/harmful.</summary>
        double Valence,

        /// <summary>Classification driving the magnitude of the reputation update.</summary>
        ThirdPartyObservationType Type) : IDomainEvent;

    /// <summary>Classification of a third-party observation.</summary>
    public enum ThirdPartyObservationType
    {
        /// <summary>A kind act (MicroPositive, help, compliment).</summary>
        PositiveAct,

        /// <summary>A hostile act (MicroNegative, insult, aggression).</summary>
        NegativeAct,

        /// <summary>A clear betrayal of trust — step-drop in Trust for the observer.</summary>
        Betrayal,

        /// <summary>
        /// A physical/sexual act witnessed between Actor and Target.
        /// Triggers jealousy in the observer when they hold romantic or sexual interest in Actor,
        /// more strongly weighted for male observers (Buss et al. 1992).
        /// </summary>
        IntimateAct,

        /// <summary>
        /// Sustained emotional intimacy (deep self-disclosure, exclusive romantic attention) witnessed
        /// between Actor and Target, absent physical/sexual content. Triggers emotional-infidelity-type
        /// jealousy, more strongly weighted for female observers (Buss et al. 1992; Harris 2003).
        /// </summary>
        EmotionalIntimacyAct
    }
}
