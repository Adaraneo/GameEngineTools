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
    /// <param name="RespondingTo">
    /// When set, the planner produces the adjacency-pair <i>response</i> to this prior act (e.g. an
    /// answer to a question, validation of a disclosure) instead of a fresh <paramref name="Intent"/> —
    /// this is what gives conversations turn-taking (model B). <c>null</c> = fresh initiating act.
    /// </param>
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
        bool Ironic = false,
        SpeechAct? RespondingTo = null);

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
    /// <param name="RequestPowerWeight">Weight of speaker power on request-verb dominance selection.</param>
    /// <param name="RequestAgreeablenessWeight">Weight of (dis)agreeableness on request-verb dominance selection.</param>
    public sealed record SpeechActPlannerConfig(
        double IntimateClosenessMin = 60.0,
        double FormalFamiliarityMax = 15.0,
        double BluntThreshold = 0.35,
        double IndirectThreshold = -0.35,
        double AgreeablenessWeight = 1.0,
        double PowerWeight = 0.8,
        double StyleWeight = 0.6,
        double UrgencyWeight = 0.7,
        double RequestPowerWeight = 0.6,
        double RequestAgreeablenessWeight = 0.4);

    /// <summary>
    /// Default <see cref="ISpeechActPlanner"/>. Register follows relationship closeness/familiarity;
    /// directness follows Brown &amp; Levinson (1987) face-threat weighting (power, urgency, low
    /// agreeableness and a direct style all push toward Blunt). Predicate choice within an act kind is
    /// deterministic (stable hash of speaker/addressee/intent/time) so the whole result is reproducible.
    /// </summary>
    public sealed class DefaultSpeechActPlanner : ISpeechActPlanner
    {
        private readonly SpeechActPlannerConfig _config;

        /// <summary>The speaker's vocabulary, or null when the acquisition layer is not wired.</summary>
        private readonly Characters.Engines.Language.ILexicalAcquisitionStore? _acquisition;

        /// <summary>Creates the planner with the given config (defaults when omitted).</summary>
        /// <param name="config">Register/directness calibration.</param>
        /// <param name="acquisition">
        /// Per-character vocabulary. Optional: without it — and with an empty one — every candidate
        /// scores the same lexically, so predicate choice is decided by dominance and the stable hash
        /// exactly as before.
        /// </param>
        public DefaultSpeechActPlanner(
            SpeechActPlannerConfig? config = null,
            Characters.Engines.Language.ILexicalAcquisitionStore? acquisition = null)
        {
            _config = config ?? new SpeechActPlannerConfig();
            _acquisition = acquisition;
        }

        /// <inheritdoc/>
        public SpeechAct Plan(SpeechActRequest request)
        {
            System.ArgumentNullException.ThrowIfNull(request);

            // Responding within a conversation ⇒ produce the adjacency-pair response rather than a fresh
            // topic; the response carries feedback/turn-management dimensions (model B).
            var response = request.RespondingTo is { } prior
                ? Conversation.AdjacencyPairResolver.ResponseTo(prior)
                : null;
            var intent = response?.Kind ?? request.Intent;

            var predicate = SelectPredicate(intent, request);

            var roles = ImmutableDictionary<FgdFunctor, EntityRef>.Empty
                .Add(FgdFunctor.ACT, request.Speaker);
            if (predicate.AddresseeRole is { } addresseeRole)
            {
                roles = roles.Add(addresseeRole, request.Addressee);
            }

            var register = ComputeRegister(request.Closeness, request.Familiarity);
            var directness = ComputeDirectness(request.Agreeableness, request.Style, request.Power, request.Urgency);
            var forceShift = request.Ironic ? new ForceShift(predicate.Point, Polarity.Negative) : null;
            var dimensions = response?.Dimensions
                ?? (intent == RelationalActKind.SmallTalk ? DialogueDimension.SocialObligation : DialogueDimension.None);

            return new SpeechAct
            {
                Point = predicate.Point,
                RelationalKind = intent,
                Dimensions = dimensions,
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

        /// <summary>
        /// Picks a candidate predicate for the resolved act kind. When the candidates carry a dominance
        /// spread (e.g. Request: požádat/vyžadovat/žebrat), the speaker's felt power drives the choice —
        /// so word choice reflects personality. Otherwise the choice is a deterministic hash.
        /// </summary>
        private SeedPredicate SelectPredicate(RelationalActKind intent, SpeechActRequest request)
        {
            var candidates = SeedPredicateLexicon.Predicates[intent];
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            // Power-driven selection when the predicates span a dominance range.
            var hasDominanceSpread = false;
            foreach (var candidate in candidates)
            {
                if (candidate.SelectionDominance != 0.0)
                {
                    hasDominanceSpread = true;
                    break;
                }
            }

            var seed = StableHash(
                $"{request.Speaker.Id.Value}|{request.Addressee.Id.Value}|{(int)intent}|{request.OccurredAt.WorldTicks}");

            // Without a vocabulary to consult, the two original branches stand exactly as they were.
            if (_acquisition is null)
            {
                return hasDominanceSpread
                    ? NearestByDominance(candidates, SpeakerDominance(request))
                    : candidates[(int)(seed % (uint)candidates.Count)];
            }

            // The two signals compose rather than compete, and they are not the same kind of signal:
            // dominance says which word the speaker WANTS, acquisition only says whether they can reach
            // for it. So acquisition filters, it does not outvote — blending them proportionally would
            // let a domineering speaker come out pleading, losing behaviour that is deliberate and tested.
            if (hasDominanceSpread)
            {
                var dominance = SpeakerDominance(request);
                var producible = new List<SeedPredicate>();

                foreach (var candidate in candidates)
                {
                    if (CanProduce(candidate.LemmaImperfective, request))
                    {
                        producible.Add(candidate);
                    }
                }

                // A domineering character still demands — but only if "vyžadovat" is a word they have.
                // Knowing none of them (an empty vocabulary included) falls back to the plain choice,
                // which is what keeps the pre-acquisition behaviour intact.
                return NearestByDominance(producible.Count > 0 ? producible : candidates, dominance);
            }

            // Elsewhere there is no "right" word to want, so availability alone shapes the choice: a
            // lemma the speaker uses readily comes out more often than one they barely have.
            var weights = new double[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                weights[i] = ProductiveAvailability(candidates[i].LemmaImperfective, request);
            }

            return WeightedStableChoice(candidates, weights, seed);
        }

        /// <summary>
        /// The speaker's felt dominance for word choice, [−1..1]: high power and low agreeableness push
        /// toward a domineering register; urgency widens the swing (an urgent low-power speaker pleads,
        /// an urgent high-power speaker demands).
        /// </summary>
        private double SpeakerDominance(SpeechActRequest request)
        {
            var basePush = _config.RequestPowerWeight * (request.Power - 0.5) * 2.0
                + _config.RequestAgreeablenessWeight * (0.5 - request.Agreeableness) * 2.0;
            var withUrgency = basePush * (1.0 + request.Urgency);   // urgency amplifies the tendency
            return Math.Clamp(withUrgency, -1.0, 1.0);
        }

        /// <summary>The candidate whose dominance sits closest to what the speaker feels.</summary>
        private static SeedPredicate NearestByDominance(IReadOnlyList<SeedPredicate> candidates, double dominance)
        {
            var best = candidates[0];
            var bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                var distance = Math.Abs(candidate.SelectionDominance - dominance);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>
        /// How readily the speaker can produce <paramref name="lemma"/> right now.
        /// </summary>
        /// <remarks>
        /// Production is harder than recognition — you understand a word long after you would still
        /// think to reach for it — so the familiarity is re-read against a shortened effective half-life
        /// before being compared to θ_P. Below that threshold a candidate keeps a small floor weight
        /// rather than zero: <c>SeedPredicateLexicon</c> is a small closed set, not an open dictionary,
        /// and a character must always have something to say.
        /// </remarks>
        private double ProductiveAvailability(string lemma, SpeechActRequest request)
        {
            // A non-human speaker has no vocabulary to consult; treat every candidate alike.
            if (!request.Speaker.Id.TryAsHumanId(out var speakerId))
            {
                return FloorWeight;
            }

            var config = _acquisition!.Config;
            var entry = _acquisition.TryGet(speakerId, lemma);
            if (entry is null)
            {
                return FloorWeight;
            }

            // Same decay curve, shorter half-life: p = 2^(−Δ/(h·factor)).
            var scaled = entry with { HalfLifeDays = entry.HalfLifeDays * config.ProductiveHalfLifeFactor };
            var productive = scaled.LexicalFamiliarity(request.OccurredAt);

            return productive >= config.ProductiveThreshold ? productive : FloorWeight;
        }

        /// <summary>
        /// Deterministic weighted choice: the seed is mapped into [0,1) and walked across the cumulative
        /// weights. No RNG — the same request always yields the same predicate.
        /// </summary>
        /// <remarks>
        /// With equal weights this reduces to <c>seed % count</c>, the pre-acquisition selection, so a
        /// character whose vocabulary says nothing about these candidates chooses exactly as before.
        /// </remarks>
        private static SeedPredicate WeightedStableChoice(
            IReadOnlyList<SeedPredicate> candidates,
            IReadOnlyList<double> weights,
            uint seed)
        {
            var total = 0.0;
            foreach (var weight in weights)
            {
                total += Math.Max(0.0, weight);
            }

            if (total <= 0.0)
            {
                return candidates[(int)(seed % (uint)candidates.Count)];
            }

            // Bucket the hash exactly as the modulo did, then place the draw INSIDE that bucket using
            // the bits the modulo threw away. Two properties fall out, and both are needed:
            //
            //   • equal weights ⇒ the winner is always the bucket itself, whatever the offset, so the
            //     pre-acquisition selection is reproduced candidate for candidate;
            //   • unequal weights ⇒ the draw ranges over the whole [0, total) span rather than sitting
            //     at one point per bucket, so a strong candidate takes a proportional share instead of
            //     swallowing every bucket. With only a handful of candidates the bucket midpoint alone
            //     is far too coarse — one well-drilled word would crowd out all the others entirely.
            var bucket = seed % (uint)candidates.Count;
            var offset = (seed / (uint)candidates.Count % 65536) / 65536.0;
            var position = ((bucket + offset) / candidates.Count) * total;

            var cumulative = 0.0;
            for (var i = 0; i < candidates.Count; i++)
            {
                cumulative += Math.Max(0.0, weights[i]);
                if (position < cumulative)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        /// <summary>True when the speaker's grip on <paramref name="lemma"/> clears the production threshold θ_P.</summary>
        private bool CanProduce(string lemma, SpeechActRequest request)
            => ProductiveAvailability(lemma, request) > FloorWeight;

        /// <summary>Floor weight for a lemma the speaker cannot readily produce — never zero.</summary>
        private const double FloorWeight = 0.05;

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
