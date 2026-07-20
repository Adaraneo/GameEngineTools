// DialogueEnums.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Contracts
{
    using System;

    /// <summary>
    /// Searle's illocutionary point — the closed, primary dimension of a <see cref="SpeechAct"/>.
    /// </summary>
    /// <remarks>
    /// Kept deliberately small and closed (Searle 1976; ISO 24617-2). Lexical variety is carried by
    /// <see cref="SpeechAct.PredicateLemma"/>, never by growing this taxonomy. <see cref="Question"/>
    /// is a pragmatically separate point (it drives the interrogative on the GM side) and is held
    /// apart from <see cref="Directive"/>.
    /// </remarks>
    public enum IllocutionaryPoint
    {
        /// <summary>Commits the speaker to the truth of a proposition (state, claim, describe).</summary>
        Assertive,

        /// <summary>Attempts to get the addressee to do something (request, command, advise).</summary>
        Directive,

        /// <summary>Commits the speaker to a future course of action (promise, offer).</summary>
        Commissive,

        /// <summary>Expresses a psychological state (thank, apologise, greet, congratulate).</summary>
        Expressive,

        /// <summary>Changes the world by being uttered (declare, name, pronounce).</summary>
        Declarative,

        /// <summary>Requests information — held separate as it governs interrogative realisation.</summary>
        Question
    }

    /// <summary>
    /// Orthogonal communicative dimensions of a dialogue act (ISO 24617-2 style). Extensible via new
    /// flags without disturbing the closed <see cref="IllocutionaryPoint"/>. A greeting, for example,
    /// is <see cref="IllocutionaryPoint.Expressive"/> carrying <see cref="SocialObligation"/>.
    /// </summary>
    [Flags]
    public enum DialogueDimension
    {
        /// <summary>No extra dimension.</summary>
        None = 0,

        /// <summary>Feedback / grounding (acknowledgement, back-channel).</summary>
        Feedback = 1,

        /// <summary>Turn management (take, keep, or yield the floor).</summary>
        TurnManagement = 2,

        /// <summary>Social-obligation management (greeting, apology, thanking).</summary>
        SocialObligation = 4
    }

    /// <summary>
    /// How bluntly the illocutionary force is put on the surface (Brown &amp; Levinson 1987 face work).
    /// </summary>
    public enum Directness
    {
        /// <summary>On-record, unmitigated (highest face threat).</summary>
        Blunt,

        /// <summary>Ordinary directness.</summary>
        Neutral,

        /// <summary>Off-record / hedged (lowest face threat).</summary>
        Indirect
    }

    /// <summary>
    /// Social register of the act. <see cref="Formal"/> maps to vykání (2pl polite) on the GM side.
    /// </summary>
    public enum Register
    {
        /// <summary>Intimate register (close relationships).</summary>
        Intimate,

        /// <summary>Informal register (tykání / familiar).</summary>
        Informal,

        /// <summary>Formal register (vykání / polite 2pl on the GM side).</summary>
        Formal
    }

    /// <summary>Polarity of the intended proposition.</summary>
    public enum Polarity
    {
        /// <summary>Affirmative.</summary>
        Affirmative,

        /// <summary>Negated.</summary>
        Negative
    }
}
