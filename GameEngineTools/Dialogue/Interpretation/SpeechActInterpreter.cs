// SpeechActInterpreter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Interpretation
{
    using System;
    using System.Collections.Immutable;
    using Grammar.Core.Enums;

    /// <summary>
    /// The listener state that shapes how an objective <see cref="SpeechAct"/> is understood.
    /// </summary>
    /// <param name="TheoryOfMindLevel">Perspective-taking depth (≥2 enables irony decoding).</param>
    /// <param name="FamiliarityWithSpeaker">Directed familiarity toward the speaker, [0..100].</param>
    /// <param name="Hostility">Relationship hostility + paranoid/dark disposition, [0..1].</param>
    /// <param name="Resolver">Optional per-listener knowledge base for resolving referents.</param>
    public sealed record ListenerContext(
        int TheoryOfMindLevel,
        double FamiliarityWithSpeaker,
        double Hostility,
        IEntityResolver? Resolver = null);

    /// <summary>
    /// Turns an objective <see cref="SpeechAct"/> into a listener-relative <see cref="PerceivedMeaning"/>.
    /// Pure and deterministic: the same (act, listener) always yields the same reading. This is the
    /// pre-step feeding the existing emotional pipeline — it is not itself an emotion appraiser.
    /// </summary>
    public interface ISpeechActInterpreter
    {
        /// <summary>Interprets <paramref name="act"/> from <paramref name="listener"/>'s point of view.</summary>
        PerceivedMeaning Appraise(SpeechAct act, ListenerContext listener);
    }

    /// <summary>Thresholds for <see cref="DefaultSpeechActInterpreter"/> — named, not magic numbers.</summary>
    /// <param name="IronyToMLevelMin">Minimum theory-of-mind level to decode irony.</param>
    /// <param name="IronyFamiliarityMin">Minimum familiarity to decode irony.</param>
    /// <param name="HostilityGain">
    /// Strength of the continuous hostile-attribution shift toward Blunt ("added negativity",
    /// van den Berg &amp; Lansu 2020) — scales how much of the listener's hostility becomes perceived
    /// directness. Continuous, not a binary threshold. Default 0.5 ⇒ one full step at hostility 1.0.
    /// </param>
    /// <param name="BaseConfidence">Confidence floor before familiarity/resolution adjustments.</param>
    /// <param name="EnableConnotationLayer">
    /// Opt-in connotation layer: when <c>false</c> (default) behaviour is byte-identical to the
    /// pre-connotation interpreter and the lexicon is never consulted.
    /// </param>
    /// <param name="ConnotationWeight">Small additive weight applied to the lemma's affect valence.</param>
    /// <param name="IronyConventionalityBypassMin">
    /// Lemma conventionality at/above which an ironic act is decoded even below the ToM/familiarity
    /// gate (Giora's Graded Salience — the ironic reading of a conventional phrase IS the salient one).
    /// </param>
    public sealed record SpeechActInterpreterConfig(
        int IronyToMLevelMin = 2,
        double IronyFamiliarityMin = 40.0,
        double HostilityGain = 0.5,
        double BaseConfidence = 0.5,
        bool EnableConnotationLayer = false,
        double ConnotationWeight = 0.15,
        double IronyConventionalityBypassMin = 0.7);

    /// <summary>
    /// Default interpreter. Irony (a <see cref="ForceShift"/>) is decoded only by a sufficiently
    /// perspective-taking, familiar listener — otherwise read literally (Crick &amp; Dodge 1994;
    /// Smeijers 2019 for the hostile-attribution shift). Confidence falls with unfamiliarity and
    /// unresolved referents.
    /// </summary>
    public sealed class DefaultSpeechActInterpreter : ISpeechActInterpreter
    {
        private readonly SpeechActInterpreterConfig _config;
        private readonly Semantics.IConnotationLexicon _lexicon;

        /// <summary>
        /// Creates the interpreter with the given config (defaults when omitted). Without a
        /// <paramref name="connotationLexicon"/> a neutral no-op lexicon is used, so existing
        /// construction sites keep working unchanged.
        /// </summary>
        public DefaultSpeechActInterpreter(
            SpeechActInterpreterConfig? config = null,
            Semantics.IConnotationLexicon? connotationLexicon = null)
        {
            _config = config ?? new SpeechActInterpreterConfig();
            _lexicon = connotationLexicon ?? Semantics.NeutralConnotationLexicon.Instance;
        }

        /// <inheritdoc/>
        public PerceivedMeaning Appraise(SpeechAct act, ListenerContext listener)
        {
            ArgumentNullException.ThrowIfNull(act);
            ArgumentNullException.ThrowIfNull(listener);

            // ── Irony / polarity: decode the shift only with enough ToM + familiarity ──
            IllocutionaryPoint perceivedPoint;
            Polarity perceivedPolarity;
            var literalMisread = false;
            if (act.ForceShift is { } shift)
            {
                var canDecode = listener.TheoryOfMindLevel >= _config.IronyToMLevelMin
                    && listener.FamiliarityWithSpeaker >= _config.IronyFamiliarityMin;
                if (canDecode)
                {
                    perceivedPoint = act.Point;
                    perceivedPolarity = act.Polarity;
                }
                else
                {
                    perceivedPoint = shift.SurfacePoint;
                    perceivedPolarity = shift.SurfacePolarity;
                    literalMisread = perceivedPolarity != act.Polarity;
                }
            }
            else
            {
                perceivedPoint = act.Point;
                perceivedPolarity = act.Polarity;
            }

            // ── Hostile attribution bias: continuous "added negativity" toward Blunt ──
            // The hostile magnitude adds to the perceived rank; no single configured threshold.
            var hostileShift = listener.Hostility * _config.HostilityGain;
            var perceivedDirectness = ShiftTowardBlunt(act.Directness, hostileShift);

            // ── Connotation layer (opt-in): additive lemma affect + conventional-irony bypass ──
            // Independent of grammatical Polarity — sentiment never leaks into sentence negation.
            var connotationDelta = 0.0;
            if (_config.EnableConnotationLayer)
            {
                var affect = _lexicon.Lookup(act.PredicateLemma);
                connotationDelta = Math.Clamp(affect.Valence * _config.ConnotationWeight, -0.3, 0.3);

                // Graded Salience (Giora): a conventionally ironic phrase is decoded even below the
                // ToM/familiarity gate — its ironic reading IS the salient one.
                if (act.ForceShift is not null && affect.Conventionality >= _config.IronyConventionalityBypassMin)
                {
                    perceivedPoint = act.Point;
                    perceivedPolarity = act.Polarity;
                    literalMisread = false;
                }
            }

            // ── Reference resolution against the listener's KB ──
            var (resolvedRoles, unresolvedFraction) = ResolveRoles(act.Roles, listener.Resolver);

            // ── Confidence: rises with familiarity, falls with unresolved refs / literal misreads ──
            var familiarityNorm = Math.Clamp(listener.FamiliarityWithSpeaker / 100.0, 0.0, 1.0);
            var confidence = Math.Clamp(_config.BaseConfidence + 0.5 * familiarityNorm - 0.5 * unresolvedFraction, 0.0, 1.0);
            if (literalMisread)
            {
                confidence *= 0.85;
            }

            return new PerceivedMeaning
            {
                Source = act,
                PerceivedPoint = perceivedPoint,
                PerceivedPolarity = perceivedPolarity,
                PerceivedDirectness = perceivedDirectness,
                ResolvedRoles = resolvedRoles,
                Confidence = confidence,
                ConnotationDelta = connotationDelta,
            };
        }

        /// <summary>
        /// Shifts directness toward Blunt by a continuous <paramref name="magnitude"/> [0..1]:
        /// the magnitude is added to the perceived rank (Indirect=0, Neutral=1, Blunt=2; one full
        /// step at magnitude 0.5) and rounded back to the discrete scale — so the transition point
        /// scales with hostility×gain instead of sitting at one configured threshold, and extreme
        /// hostility can shift Indirect all the way to Blunt.
        /// </summary>
        private static Directness ShiftTowardBlunt(Directness directness, double magnitude)
        {
            var rank = DirectnessRank(directness) + Math.Max(0.0, magnitude) * 2.0;
            return (int)Math.Round(rank, MidpointRounding.AwayFromZero) switch
            {
                <= 0 => Directness.Indirect,
                1 => Directness.Neutral,
                _ => Directness.Blunt,
            };
        }

        private static int DirectnessRank(Directness directness) => directness switch
        {
            Directness.Indirect => 0,
            Directness.Neutral => 1,
            _ => 2,
        };

        private static (ImmutableDictionary<FgdFunctor, EntityRef> Resolved, double UnresolvedFraction) ResolveRoles(
            ImmutableDictionary<FgdFunctor, EntityRef> roles,
            IEntityResolver? resolver)
        {
            if (resolver is null || roles.Count == 0)
            {
                return (roles, 0.0);
            }

            var unresolved = 0;
            foreach (var role in roles)
            {
                // Human referents that the listener cannot place lower confidence; object refs pass through.
                if (role.Value.Id.Kind == EntityKind.Human && !resolver.TryResolveHuman(role.Value, out _))
                {
                    unresolved++;
                }
            }

            return (roles, (double)unresolved / roles.Count);
        }
    }
}
