// SpeechActPlanner.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Dialogue.Planning
{
    using System.Collections.Immutable;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Seed;
    using GameEngineTools.World.Utils.Time;
    using Grammar.Core.Enums;

    /// <summary>
    /// Speaker-side intent handed to the <see cref="ISpeechActPlanner"/>: a chosen relational act
    /// plus the speaker/addressee references and the state needed to shape register and directness.
    /// The addressee's <see cref="EntityRef.LemmaSnapshot"/> carries its name so the GM side can later
    /// realise a direct-address utterance (mode 2), e.g. "Jano, …".
    /// </summary>
    /// <param name="Intent">Relational act kind the speaker wants to perform.</param>
    /// <param name="Speaker">Speaker reference (id + name lemma).</param>
    /// <param name="Addressee">Addressee reference (id + name lemma).</param>
    /// <param name="OccurredAt">World time of the act.</param>
    /// <param name="Closeness">Directed closeness speaker→addressee [0..100].</param>
    /// <param name="Familiarity">Directed familiarity speaker→addressee [0..100].</param>
    /// <param name="Agreeableness">Speaker Big-Five Agreeableness [0..1].</param>
    /// <param name="Style">Speaker's preferred communication style.</param>
    /// <param name="Power">Speaker's felt power/dominance over the addressee [0..1].</param>
    /// <param name="Urgency">How pressing the act is [0..1]; raises face-threat tolerance.</param>
    /// <param name="Ironic">When <c>true</c>, attach a <see cref="ForceShift"/> (opposite surface polarity).</param>
    public sealed record SpeechActRequest(
        RelationalActKind Intent,
        EntityRef Speaker,
        EntityRef Addressee,
        WDateTime OccurredAt,
        double Closeness,
        double Familiarity,
        double Agreeableness,
        CommunicationStyle Style,
        double Power,
        double Urgency = 0.0,
        bool Ironic = false);

    /// <summary>
    /// Deterministic, pure speaker-side service that turns a <see cref="SpeechActRequest"/> into a fully
    /// specified <see cref="SpeechAct"/> — including Register, Directness, Speaker and Addressee so the
    /// act can be realised as a direct-address utterance (mode 2), not only a third-person gloss.
    /// This is the single intended author of dialogue <see cref="SpeechAct"/>s.
    /// </summary>
    public interface ISpeechActPlanner
    {
        /// <summary>Plans a <see cref="SpeechAct"/> for the given request. Same request ⇒ identical act.</summary>
        SpeechAct Plan(SpeechActRequest request);
    }

    /// <summary>
    /// Tunables for <see cref="DefaultSpeechActPlanner"/>. All thresholds are named (no magic numbers)
    /// so calibration lives in one place.
    /// </summary>
    /// <param name="IntimateClosenessMin">At/above this closeness the register is Intimate.</param>
    /// <param name="FormalFamiliarityMax">Below this familiarity the register is Formal (social distance).</param>
    /// <param name="BluntThreshold">Directness score at/above which the act is Blunt.</param>
    /// <param name="IndirectThreshold">Directness score at/below which the act is Indirect.</param>
    /// <param name="AgreeablenessWeight">Weight of (dis)agreeableness on directness (high A ⇒ indirect).</param>
    /// <param name="PowerWeight">Weight of speaker power on directness (high power ⇒ blunt).</param>
    /// <param name="StyleWeight">Weight of communication style on directness.</param>
    /// <param name="UrgencyWeight">Weight of urgency on directness (urgent ⇒ blunt).</param>
    public sealed record SpeechActPlannerConfig(
        double IntimateClosenessMin = 60.0,
        double FormalFamiliarityMax = 15.0,
        double BluntThreshold = 0.35,
        double IndirectThreshold = -0.35,
        double AgreeablenessWeight = 1.0,
        double PowerWeight = 0.8,
        double StyleWeight = 0.6,
        double UrgencyWeight = 0.7);

    /// <summary>
    /// Default <see cref="ISpeechActPlanner"/>. Register follows relationship closeness/familiarity;
    /// directness follows Brown &amp; Levinson (1987) face-threat weighting (power, urgency, low
    /// agreeableness and a direct style all push toward Blunt). Predicate choice within an act kind is
    /// deterministic (stable hash of speaker/addressee/intent/time) so the whole result is reproducible.
    /// </summary>
    public sealed class DefaultSpeechActPlanner : ISpeechActPlanner
    {
        private readonly SpeechActPlannerConfig _config;

        /// <summary>Creates the planner with the given config (defaults when omitted).</summary>
        public DefaultSpeechActPlanner(SpeechActPlannerConfig? config = null)
            => _config = config ?? new SpeechActPlannerConfig();

        /// <inheritdoc/>
        public SpeechAct Plan(SpeechActRequest request)
        {
            System.ArgumentNullException.ThrowIfNull(request);

            var predicate = SelectPredicate(request);

            var roles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty
                .Add(FgdFunctor.ACT, request.Speaker);
            if (predicate.AddresseeRole is { } addresseeRole)
            {
                roles = roles.Add(addresseeRole, request.Addressee);
            }

            var register = ComputeRegister(request.Closeness, request.Familiarity);
            var directness = ComputeDirectness(request.Agreeableness, request.Style, request.Power, request.Urgency);
            var forceShift = request.Ironic ? new ForceShift(predicate.Point, Polarity.Negative) : null;

            return new SpeechAct
            {
                Point = predicate.Point,
                RelationalKind = request.Intent,
                Dimensions = request.Intent == RelationalActKind.SmallTalk
                    ? DialogueDimension.SocialObligation
                    : DialogueDimension.None,
                PredicateLemma = predicate.LemmaImperfective,
                Roles = roles,
                Polarity = Polarity.Affirmative,
                Register = register,
                Directness = directness,
                ForceShift = forceShift,
                Speaker = request.Speaker,
                Addressee = request.Addressee,
                OccurredAt = request.OccurredAt,
            };
        }

        /// <summary>Register from relationship state: distant ⇒ Formal, close ⇒ Intimate, else Informal.</summary>
        private Register ComputeRegister(double closeness, double familiarity)
        {
            if (familiarity < _config.FormalFamiliarityMax)
            {
                return Register.Formal;
            }

            return closeness >= _config.IntimateClosenessMin ? Register.Intimate : Register.Informal;
        }

        /// <summary>
        /// Directness via a Brown &amp; Levinson face-threat score: a direct/low-context style, low
        /// agreeableness, high power and high urgency all raise it toward Blunt.
        /// </summary>
        private Directness ComputeDirectness(double agreeableness, CommunicationStyle style, double power, double urgency)
        {
            var styleSign = style switch
            {
                CommunicationStyle.Direct or CommunicationStyle.LowContext => 1.0,
                CommunicationStyle.Indirect or CommunicationStyle.HighContext => -1.0,
                _ => 0.0,
            };

            var score =
                _config.StyleWeight * styleSign
                + _config.AgreeablenessWeight * (0.5 - agreeableness) * 2.0
                + _config.PowerWeight * (power - 0.5) * 2.0
                + _config.UrgencyWeight * urgency;

            if (score >= _config.BluntThreshold)
            {
                return Directness.Blunt;
            }

            return score <= _config.IndirectThreshold ? Directness.Indirect : Directness.Neutral;
        }

        /// <summary>Deterministically picks one candidate predicate for the requested act kind.</summary>
        private static SeedPredicate SelectPredicate(SpeechActRequest request)
        {
            var candidates = SeedPredicateLexicon.Predicates[request.Intent];
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            var seed = StableHash(
                $"{request.Speaker.Id.Value}|{request.Addressee.Id.Value}|{(int)request.Intent}|{request.OccurredAt.WorldTicks}");
            return candidates[(int)(seed % (uint)candidates.Count)];
        }

        /// <summary>FNV-1a 32-bit — a stable hash (unlike string.GetHashCode) so choices reproduce across runs.</summary>
        private static uint StableHash(string value)
        {
            const uint OffsetBasis = 2166136261;
            const uint Prime = 16777619;
            var hash = OffsetBasis;
            foreach (var ch in value)
            {
                hash = (hash ^ ch) * Prime;
            }

            return hash;
        }
    }
}
