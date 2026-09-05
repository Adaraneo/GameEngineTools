// LexicalAcquisitionWiringTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Phases 1 and 3: reinforcement wired into <see cref="DefaultInteractionEngine"/>, and the social
    /// amplification of how hard a heard word sticks.
    /// </summary>
    /// <remarks>
    /// The engine is the single place either party learns a word. It handles
    /// <c>InteractionProposed</c> under a <c>p.To == ctx.Id</c> guard, so it runs exactly once per
    /// utterance — unlike the listener-side interpreters (Psychology and Memory), which each appraise
    /// the same act independently and would therefore double-count.
    /// </remarks>
    [TestClass]
    public class LexicalAcquisitionWiringTests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("aaaaaaaa-4444-4444-4444-444444444444"));
        private static readonly HumanId Listener = new(Guid.Parse("bbbbbbbb-5555-5555-5555-555555555555"));
        private const string Lemma = "chválit";

        private static WDateTime At(int minutes)
            => new(WDateTime.New(WDateOnly.New(100, 1, 1)).WorldTicks + WTimeSpan.FromMinutes(minutes).Ticks);

        // ──────────────────────────────────────────────────────────────────────
        // Phase 1 — both sides learn, exactly once
        // ──────────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Handle_Utterance_TeachesBothSpeakerAndListener()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);
            var ctx = BuildContext(Listener);

            engine.Handle(Proposal(At(0)), ctx, new EventCollector());

            Assert.IsTrue(store.LexicalFamiliarity(Speaker, Lemma, At(0)) > 0.0, "the speaker practised the word");
            Assert.IsTrue(store.LexicalFamiliarity(Listener, Lemma, At(0)) > 0.0, "the listener picked it up");
        }

        [TestMethod]
        public void Handle_Utterance_CountsOncePerSide()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);
            var ctx = BuildContext(Listener);

            engine.Handle(Proposal(At(0)), ctx, new EventCollector());

            Assert.AreEqual(1, store.TryGet(Speaker, Lemma)!.TimesSeen, "one utterance is one exposure for the speaker");
            Assert.AreEqual(1, store.TryGet(Listener, Lemma)!.TimesSeen, "and one for the listener");
            Assert.AreEqual(2, store.Count, "exactly two records — no third party learned anything");
        }

        [TestMethod]
        public void Handle_Repetition_StrengthensBothVocabularies()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);

            // Accepting, so the speaker's use actually lands — see the rejection case below.
            var ctx = BuildContext(Listener, random: new AlwaysAcceptRandom());

            engine.Handle(Proposal(At(0)), ctx, new EventCollector());
            var speakerAfterOne = store.TryGet(Speaker, Lemma)!.HalfLifeDays;
            var listenerAfterOne = store.TryGet(Listener, Lemma)!.HalfLifeDays;

            for (var i = 1; i <= 6; i++)
            {
                engine.Handle(Proposal(At(i * 10)), ctx, new EventCollector());
            }

            Assert.IsTrue(store.TryGet(Speaker, Lemma)!.HalfLifeDays > speakerAfterOne, "practice that works sticks");
            Assert.IsTrue(store.TryGet(Listener, Lemma)!.HalfLifeDays > listenerAfterOne, "so does repeated hearing");
        }

        // Success for the speaker is whether the interaction was accepted, so a predicate that keeps
        // drawing rebuffs earns no retention — repetition alone is not practice.
        [TestMethod]
        public void Handle_RepeatedlyRebuffedWord_DoesNotStickForTheSpeaker()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);
            var ctx = BuildContext(Listener);   // ZeroRandom declines everything

            engine.Handle(Proposal(At(0)), ctx, new EventCollector());
            var afterOne = store.TryGet(Speaker, Lemma)!.HalfLifeDays;

            for (var i = 1; i <= 6; i++)
            {
                engine.Handle(Proposal(At(i * 10)), ctx, new EventCollector());
            }

            Assert.IsTrue(
                store.TryGet(Speaker, Lemma)!.HalfLifeDays <= afterOne,
                "a word that never lands must not be retained better for having been tried more often");

            // The listener still learns it: hearing is comprehension, not approval.
            Assert.IsTrue(store.LexicalFamiliarity(Listener, Lemma, At(60)) > 0.0);
        }

        [TestMethod]
        public void Handle_Utterance_ListenerRecordsProvenance_SpeakerDoesNot()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);
            var ctx = BuildContext(Listener);

            engine.Handle(Proposal(At(0)), ctx, new EventCollector());

            Assert.AreEqual(Speaker, store.TryGet(Listener, Lemma)!.LearnedFrom, "the listener learned it from the speaker");
            Assert.IsNull(store.TryGet(Speaker, Lemma)!.LearnedFrom, "the speaker already knew it — using it is not learning");
        }

        [TestMethod]
        public void Handle_WithoutStore_ChangesNothingAndDoesNotThrow()
        {
            // The whole layer is opt-in: an engine built the old way must behave exactly as before.
            var engine = BuildEngine(store: null);
            var ctx = BuildContext(Listener);
            var outbox = new EventCollector();

            engine.Handle(Proposal(At(0)), ctx, outbox);

            Assert.IsTrue(outbox.Drain().Count > 0, "the interaction still resolves normally");
        }

        [TestMethod]
        public void Handle_ActWithoutPredicate_TeachesNothing()
        {
            var store = new DefaultLexicalAcquisitionStore();
            var engine = BuildEngine(store);
            var ctx = BuildContext(Listener);

            // InteractionProposed.Of leaves PredicateLemma empty — a structural act with no word in it.
            engine.Handle(InteractionProposed.Of(At(0), Speaker, Listener, RelationalActKind.SmallTalk), ctx, new EventCollector());

            Assert.AreEqual(0, store.Count);
        }

        // ──────────────────────────────────────────────────────────────────────
        // Phase 3 — who said it changes how hard it sticks
        // ──────────────────────────────────────────────────────────────────────

        // Closeness and PerceivedDominance are varied while Familiarity and Trust are held fixed, because
        // only the latter two feed listener confidence — so any difference below is the social gain
        // alone, not a change in whether the word was understood.

        [TestMethod]
        public void Handle_CloseSpeaker_IsPickedUpFasterThanNeutralOne()
        {
            var neutral = LearnOnce(Closeness: 0, PerceivedDominance: 50);
            var close = LearnOnce(Closeness: 90, PerceivedDominance: 50);

            Assert.IsTrue(
                close > neutral,
                $"words from someone we are close to take hold faster ({neutral:F3} → {close:F3} days)");
        }

        [TestMethod]
        public void Handle_HigherStandingSpeaker_IsPickedUpFasterThanLowerStandingOne()
        {
            var below = LearnOnce(Closeness: 50, PerceivedDominance: 5);
            var above = LearnOnce(Closeness: 50, PerceivedDominance: 95);

            Assert.IsTrue(
                above > below,
                $"accommodation runs upward — toward those seen as above us ({below:F3} → {above:F3} days)");
        }

        [TestMethod]
        public void Handle_SocialGain_IsCappedAgainstOverAccommodation()
        {
            var config = new LexicalAcquisitionConfig(CatOverAccommodationCap: 1.0);
            var capped = new DefaultLexicalAcquisitionStore(config);
            var uncapped = new DefaultLexicalAcquisitionStore(new LexicalAcquisitionConfig(CatOverAccommodationCap: 2.5));

            // The most amplifying speaker possible: maximally close and maximally dominant.
            var ctx = BuildContext(Listener, Closeness: 100, PerceivedDominance: 100);
            BuildEngine(capped).Handle(Proposal(At(0)), ctx, new EventCollector());
            BuildEngine(uncapped).Handle(Proposal(At(0)), ctx, new EventCollector());

            Assert.IsTrue(
                capped.TryGet(Listener, Lemma)!.HalfLifeDays < uncapped.TryGet(Listener, Lemma)!.HalfLifeDays,
                "the cap must actually bind — otherwise one admired speaker floods everyone's vocabulary");
        }

        [TestMethod]
        public void Handle_UnknownSpeaker_GetsNeutralAmplification()
        {
            // No edge at all (a stranger): amplification is neither boosted nor penalised.
            var stranger = new DefaultLexicalAcquisitionStore();
            BuildEngine(stranger).Handle(Proposal(At(0)), BuildContext(Listener), new EventCollector());

            var neutralEdge = new DefaultLexicalAcquisitionStore();
            BuildEngine(neutralEdge).Handle(
                Proposal(At(0)), BuildContext(Listener, Closeness: 0, PerceivedDominance: 50), new EventCollector());

            Assert.AreEqual(
                neutralEdge.TryGet(Listener, Lemma)!.HalfLifeDays,
                stranger.TryGet(Listener, Lemma)!.HalfLifeDays,
                1e-9);
        }

        /// <summary>Hears the lemma once from a speaker with the given edge; returns the resulting half-life.</summary>
        private static double LearnOnce(double Closeness, double PerceivedDominance)
        {
            var store = new DefaultLexicalAcquisitionStore();
            var ctx = BuildContext(Listener, Closeness, PerceivedDominance);
            BuildEngine(store).Handle(Proposal(At(0)), ctx, new EventCollector());
            return store.TryGet(Listener, Lemma)!.HalfLifeDays;
        }

        // ──────────────────────────────────────────────────────────────────────
        // Helpers
        // ──────────────────────────────────────────────────────────────────────

        private static InteractionProposed Proposal(WDateTime when)
        {
            var act = SpeechAct.Relational(RelationalActKind.SmallTalk, Speaker, Listener, when)
                with
            { PredicateLemma = Lemma };
            return new InteractionProposed(when, Speaker, Listener, new InteractionContent(act), SexBiology.Male);
        }

        private static DefaultInteractionEngine BuildEngine(ILexicalAcquisitionStore? store)
        {
            var engine = new DefaultInteractionEngine(
                Options.Create(new InteractionConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                store);

            engine.RestoreState(new InteractionSurface("room", false, 0.1, 0.1, SurfaceKind.Social));
            return engine;
        }

        /// <summary>Accepts every interaction, so a speaker's use counts as successful.</summary>
        private sealed class AlwaysAcceptRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => true;
        }

        private static IHumanContext BuildContext(
            HumanId self,
            double? Closeness = null,
            double? PerceivedDominance = null,
            IRandomSource? random = null)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var edges = new Dictionary<HumanId, RelationshipEdge>();
            if (Closeness is { } c && PerceivedDominance is { } d)
            {
                // Familiarity and Trust are fixed across every case so listener confidence — and thus
                // whether the word counts as understood — cannot vary with the social gain under test.
                edges[Speaker] = new RelationshipEdge(
                    self, Speaker, Like: 50, Trust: 50, Familiarity: 50,
                    AestheticAttraction: 50, PhysicalAttraction: 50, IntimateAffinity: 20, SexualInterest: 20,
                    Closeness: c, Respect: 50, Comfort: 50,
                    PerceivedDominance: d,
                    Breakdown: new DomainBreakdown(50, 50, 50, 50, 50));
            }

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("room", false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(edges),
                new MemoryIndex(new List<EpisodicMemory>()));

            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random ?? new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };
        }
    }
}
