// LexicalAcquisition.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Language
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// One character's grip on one lemma: how well they know a word, and how fast they are losing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Half-life regression (Settles &amp; Meeder 2016): recall decays exponentially from the last
    /// reinforcement, and each successful use lengthens the half-life. Familiarity is therefore computed
    /// <b>lazily</b> from <see cref="LastReinforced"/> rather than advanced per tick — nothing has to
    /// walk every character's vocabulary every frame, and the value is identical whether a character was
    /// simulated at Player or Background cadence.
    /// </para>
    /// <para>
    /// Not to be confused with <c>RelationshipEdge.Familiarity</c> or
    /// <c>SpeechActRequest.Familiarity</c>, which mean "how well A knows B" on a 0–100 scale. This is
    /// how well someone knows a <i>word</i>, on 0–1.
    /// </para>
    /// </remarks>
    /// <param name="Lemma">The lemma this record is about.</param>
    /// <param name="FirstEncountered">When the character first met the word.</param>
    /// <param name="LastReinforced">When it was last used or heard — decay is measured from here.</param>
    /// <param name="HalfLifeDays">Days after which recall falls to one half.</param>
    /// <param name="TimesSeen">Total exposures, successful or not.</param>
    /// <param name="TimesCorrect">Exposures that landed (understood, or used and accepted).</param>
    /// <param name="TimesIncorrect">Exposures that did not.</param>
    /// <param name="LearnedFrom">Who the word came from, when it was learned rather than produced.</param>
    public sealed record LexicalAcquisition(
        string Lemma,
        WDateTime FirstEncountered,
        WDateTime LastReinforced,
        double HalfLifeDays,
        int TimesSeen = 0,
        int TimesCorrect = 0,
        int TimesIncorrect = 0,
        HumanId? LearnedFrom = null)
    {
        /// <summary>
        /// Recall probability at <paramref name="now"/> — Settles &amp; Meeder 2016: p = 2^(−Δ/h).
        /// </summary>
        /// <param name="now">The instant to evaluate at.</param>
        /// <returns>Familiarity in [0, 1]; 1 at the moment of reinforcement, halving every half-life.</returns>
        public double LexicalFamiliarity(WDateTime now)
        {
            // WTimeSpan has TotalSeconds/Minutes/Hours/Days — there is no TotalGameDays. Difference()
            // works without an initialised WWorld.Spec, unlike the calendar-dependent members.
            var deltaDays = WDateTime.Difference(now, LastReinforced).TotalDays;

            // Guard the degenerate cases rather than trusting callers: a non-positive half-life would
            // divide by zero, and a clock that ran backwards would report better-than-perfect recall.
            if (HalfLifeDays <= 0.0)
            {
                return 0.0;
            }

            if (deltaDays <= 0.0)
            {
                return 1.0;
            }

            return Math.Clamp(Math.Pow(2.0, -deltaDays / HalfLifeDays), 0.0, 1.0);
        }
    }
}
