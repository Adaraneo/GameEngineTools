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
    /// <param name="HostileAttributionThreshold">Hostility at/above which directness shifts toward Blunt.</param>
    /// <param name="BaseConfidence">Confidence floor before familiarity/resolution adjustments.</param>
    public sealed record SpeechActInterpreterConfig(
        int IronyToMLevelMin = 2,
        double IronyFamiliarityMin = 40.0,
        double HostileAttributionThreshold = 0.55,
        double BaseConfidence = 0.5);

    /// <summary>
    /// Default interpreter. Irony (a <see cref="ForceShift"/>) is decoded only by a sufficiently
    /// perspective-taking, familiar listener — otherwise read literally (Crick &amp; Dodge 1994;
    /// Smeijers 2019 for the hostile-attribution shift). Confidence falls with unfamiliarity and
    /// unresolved referents.
    /// </summary>
    public sealed class DefaultSpeechActInterpreter : ISpeechActInterpreter
    {
        private readonly SpeechActInterpreterConfig _config;

        /// <summary>Creates the interpreter with the given config (defaults when omitted).</summary>
        public DefaultSpeechActInterpreter(SpeechActInterpreterConfig? config = null)
            => _config = config ?? new SpeechActInterpreterConfig();

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

            // ── Hostile attribution bias: shift directness one step toward Blunt ──
            var perceivedDirectness = listener.Hostility >= _config.HostileAttributionThreshold
                ? ShiftTowardBlunt(act.Directness)
                : act.Directness;

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
            };
        }

        private static Directness ShiftTowardBlunt(Directness directness) => directness switch
        {
            Directness.Indirect => Directness.Neutral,
            Directness.Neutral => Directness.Blunt,
            _ => Directness.Blunt,
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
