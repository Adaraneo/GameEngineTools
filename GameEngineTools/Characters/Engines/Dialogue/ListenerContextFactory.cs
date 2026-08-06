// ListenerContextFactory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Dialogue
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Dialogue.Interpretation;

    /// <summary>
    /// Builds a <see cref="ListenerContext"/> from a character's live context, so every engine that
    /// interprets an incoming act (Psychology for emotion, Memory for the subjective trace) derives the
    /// <i>same</i> listener state and therefore the same <c>PerceivedMeaning</c>.
    /// </summary>
    public static class ListenerContextFactory
    {
        /// <summary>
        /// Derives the listener state of <paramref name="ctx"/> toward <paramref name="speaker"/>:
        /// familiarity and hostility from the directed relationship edge plus the dark/paranoid
        /// disposition, and a theory-of-mind proxy from Openness (cognitive perspective-taking).
        /// </summary>
        public static ListenerContext For(IHumanContext ctx, HumanId speaker)
        {
            ArgumentNullException.ThrowIfNull(ctx);

            var edge = ctx.Snapshot.Relationships.Edges.GetValueOrDefault(speaker);
            var familiarity = edge?.Familiarity ?? 0.0;
            var trust = edge?.Trust ?? 50.0;
            var darkCore = ctx.Personality.DarkCore?.DarkCore ?? 0.0;

            // Only genuinely low trust (< 40) contributes, so ordinary relationships stay non-hostile.
            var trustHostility = Math.Clamp((40.0 - trust) / 40.0, 0.0, 1.0);
            var hostility = Math.Clamp(0.55 * darkCore + 0.55 * trustHostility, 0.0, 1.0);

            // The character's own Theory-of-Mind recursion ceiling (Kinderman 1998; default 4).
            // ListenerId lets the interpreter consult THIS listener's vocabulary — without it, decoding
            // falls back to what the population knows on average.
            return new ListenerContext(
                ctx.Personality.ToMCeiling, familiarity, hostility, Resolver: null, ListenerId: ctx.Id);
        }
    }
}
