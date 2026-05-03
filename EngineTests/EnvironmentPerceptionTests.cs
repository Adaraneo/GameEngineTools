// EnvironmentPerceptionTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Tests for E1 (proxemics), E2 (noise misattribution), E3 (privacy non-monotonicity),
    /// E4 (Neuroticism amplification of environment stress).
    /// </summary>
    [TestClass]
    public sealed class EnvironmentPerceptionTests : TestBase
    {
        private static readonly PsychologyConfig NoiselessCfg = new PsychologyConfig(
            BaselineAffectVariance: 0.0,
            StressRecoveryRatePerHour: 0.0,
            EnableCircadianRhythm: false);

        // ── E1 · Proxemics zone ─────────────────────────────────────────────

        [TestMethod]
        public void E1_IntimateZoneViolation_AddsStress()
        {
            // Being in the intimate zone (<0.45m) without privacy should raise stress.
            var engine = BuildPsychEngine();
            var stressBefore = engine.State.Stress;

            var ctx = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Social,
                proxemicDistance: 0.30);  // inside intimate zone

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctx, new EventCollector());

            Assert.IsTrue(engine.State.Stress > stressBefore,
                $"Intimate zone violation should raise stress (before={stressBefore:F2}, after={engine.State.Stress:F2})");
        }

        [TestMethod]
        public void E1_PublicZone_NoExtraStress_FromProxemics()
        {
            // Being > 3.6m away should not add proxemics stress.
            // Use identical surfaces so E3 privacy mismatch is equal for both.
            var engineFar   = BuildPsychEngine();
            var engineNoPos = BuildPsychEngine();

            var ctxFar   = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Public,
                proxemicDistance: 5.0);
            // Same surface as ctxFar but no proxemic distance — only difference is distance
            var ctxNoPos = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Public,
                proxemicDistance: null);

            var outbox = new EventCollector();
            engineFar.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxFar, outbox);
            engineNoPos.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxNoPos, outbox);

            Assert.AreEqual(engineFar.State.Stress, engineNoPos.State.Stress, 0.1,
                "Public zone distance should produce same stress as no proxemics data");
        }

        [TestMethod]
        public void E1_IntimateZoneWithPrivacy_NoStress()
        {
            // With HasPrivacy=true (e.g. partner in private room), intimate zone is NOT a violation.
            var enginePrivate = BuildPsychEngine();
            var enginePublic  = BuildPsychEngine();

            var ctxPrivate = BuildCtx(hasPrivacy: true, surfaceKind: SurfaceKind.Private,
                proxemicDistance: 0.20);
            var ctxPublic  = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Social,
                proxemicDistance: 0.20);

            var outbox = new EventCollector();
            enginePrivate.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxPrivate, outbox);
            enginePublic.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctxPublic, outbox);

            Assert.IsTrue(enginePublic.State.Stress > enginePrivate.State.Stress,
                $"Public intimate zone should produce more stress than private (pub={enginePublic.State.Stress:F2}, priv={enginePrivate.State.Stress:F2})");
        }

        // ── E2 · Noise → misattribution ─────────────────────────────────────

        [TestMethod]
        public void E2_HighNoise_IncreasesBaseAcceptancePenalty()
        {
            // In a noisy environment, more interactions should be misattributed (rejected).
            // Test: same character, stressed context, high noise vs quiet — acceptance rate differs.
            var cfg = new InteractionConfig();
            var engineQuiet = new DefaultInteractionEngine(Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));
            var engineNoisy = new DefaultInteractionEngine(Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

            var self     = new HumanId(Guid.NewGuid());
            var proposer = new HumanId(Guid.NewGuid());
            var rng      = new SeededRandom(42);

            engineQuiet.Handle(new ContextChanged(WDateTime.New(100, 1, 1), self,
                "loc", false, 0.05, 0.3, SurfaceKind.Social), BuildMinCtx(self), new EventCollector());
            engineNoisy.Handle(new ContextChanged(WDateTime.New(100, 1, 1), self,
                "loc", false, 0.90, 0.3, SurfaceKind.Social), BuildMinCtx(self), new EventCollector());

            var outboxQ = new EventCollector();
            var outboxN = new EventCollector();

            // Use a stressed context so misattribution has room to act
            var ctxQ = BuildMinCtxWithStress(self, stress: 60, random: rng);
            var ctxN = BuildMinCtxWithStress(self, stress: 60, random: new SeededRandom(42));

            for (var i = 0; i < 200; i++)
            {
                var ev = new InteractionProposed(WDateTime.New(100, 1, 1), proposer, self,
                    SpeechAct.Invite, null);
                engineQuiet.Handle(ev, ctxQ, outboxQ);
                engineNoisy.Handle(ev, ctxN, outboxN);
            }

            var quietAccepted = outboxQ.Drain().OfType<InteractionOutcome>()
                                        .Count(o => o.Accepted);
            var noisyAccepted = outboxN.Drain().OfType<InteractionOutcome>()
                                        .Count(o => o.Accepted);

            Assert.IsTrue(noisyAccepted < quietAccepted,
                $"High noise should reduce acceptance rate (quiet={quietAccepted}, noisy={noisyAccepted})");
        }

        // ── E3 · Privacy non-monotonicity ────────────────────────────────────

        [TestMethod]
        public void E3_IntrovertInPublic_HigherStress_ThanInPrivate()
        {
            // Introvert (E=0.1) in public → strong privacy mismatch → more stress
            var enginePublic  = BuildPsychEngine(extraversion: 0.1);
            var enginePrivate = BuildPsychEngine(extraversion: 0.1);

            var ctxPublic  = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Public,
                extraversion: 0.1);
            var ctxPrivate = BuildCtx(hasPrivacy: true,  surfaceKind: SurfaceKind.Private,
                extraversion: 0.1);

            var outbox = new EventCollector();
            enginePublic.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxPublic, outbox);
            enginePrivate.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxPrivate, outbox);

            Assert.IsTrue(enginePublic.State.Stress > enginePrivate.State.Stress,
                $"Introvert in public should have higher stress than in private " +
                $"(pub={enginePublic.State.Stress:F2}, priv={enginePrivate.State.Stress:F2})");
        }

        [TestMethod]
        public void E3_ExtrovertInIsolation_HigherStress_ThanInPublic()
        {
            // Extravert (E=0.9) in isolation (HasPrivacy=true, Private) → stress from over-privacy
            var enginePublic   = BuildPsychEngine(extraversion: 0.9);
            var engineIsolated = BuildPsychEngine(extraversion: 0.9);

            var ctxPublic   = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Social,
                extraversion: 0.9);
            var ctxIsolated = BuildCtx(hasPrivacy: true,  surfaceKind: SurfaceKind.Private,
                extraversion: 0.9);

            var outbox = new EventCollector();
            enginePublic.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxPublic, outbox);
            engineIsolated.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(3), ctxIsolated, outbox);

            Assert.IsTrue(engineIsolated.State.Stress > enginePublic.State.Stress,
                $"Extravert in isolation should have higher stress than in social context " +
                $"(social={enginePublic.State.Stress:F2}, isolated={engineIsolated.State.Stress:F2})");
        }

        // ── E4 · Neuroticism amplifies environment stress ─────────────────────

        [TestMethod]
        public void E4_HighNeuroticism_HigherStress_FromZoneViolation()
        {
            // High-N character should accumulate more stress from intimate zone violation.
            var engineLowN  = BuildPsychEngine(neuroticism: 0.1);
            var engineHighN = BuildPsychEngine(neuroticism: 0.9);

            var ctxLowN  = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Social,
                proxemicDistance: 0.30, neuroticism: 0.1);
            var ctxHighN = BuildCtx(hasPrivacy: false, surfaceKind: SurfaceKind.Social,
                proxemicDistance: 0.30, neuroticism: 0.9);

            var outbox = new EventCollector();
            engineLowN.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(2), ctxLowN, outbox);
            engineHighN.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(2), ctxHighN, outbox);

            Assert.IsTrue(engineHighN.State.Stress > engineLowN.State.Stress,
                $"High-N should accumulate more zone-violation stress (lowN={engineLowN.State.Stress:F2}, highN={engineHighN.State.Stress:F2})");
        }

        // ── ProxemicsHelper unit tests ────────────────────────────────────────

        [TestMethod]
        public void ProxemicsHelper_GetZone_CorrectlyClassifies()
        {
            Assert.AreEqual(ProxemicsZone.Intimate, ProxemicsHelper.GetZone(0.30));
            Assert.AreEqual(ProxemicsZone.Intimate, ProxemicsHelper.GetZone(0.44));
            Assert.AreEqual(ProxemicsZone.Personal, ProxemicsHelper.GetZone(0.45));
            Assert.AreEqual(ProxemicsZone.Personal, ProxemicsHelper.GetZone(1.19));
            Assert.AreEqual(ProxemicsZone.Social, ProxemicsHelper.GetZone(1.20));
            Assert.AreEqual(ProxemicsZone.Social, ProxemicsHelper.GetZone(3.59));
            Assert.AreEqual(ProxemicsZone.Public, ProxemicsHelper.GetZone(3.60));
            Assert.AreEqual(ProxemicsZone.Public, ProxemicsHelper.GetZone(10.0));
        }

        [TestMethod]
        public void ProxemicsHelper_IsZoneViolation_IntimateWithoutPrivacy_IsViolation()
        {
            Assert.IsTrue(ProxemicsHelper.IsZoneViolation(
                ProxemicsZone.Intimate, hasPrivacy: false, SurfaceKind.Social));
        }

        [TestMethod]
        public void ProxemicsHelper_IsZoneViolation_IntimateWithPrivacy_IsNotViolation()
        {
            Assert.IsFalse(ProxemicsHelper.IsZoneViolation(
                ProxemicsZone.Intimate, hasPrivacy: true, SurfaceKind.Private));
        }

        [TestMethod]
        public void ProxemicsHelper_IsZoneViolation_SocialZone_IsNotViolation()
        {
            Assert.IsFalse(ProxemicsHelper.IsZoneViolation(
                ProxemicsZone.Social, hasPrivacy: false, SurfaceKind.Public));
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static DefaultPsychologyEngine BuildPsychEngine(
            double neuroticism = 0.5, double extraversion = 0.5)
        {
            var engine = new DefaultPsychologyEngine(
                Options.Create(NoiselessCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());
            engine.RestoreState(engine.State with
            {
                Stress = 20,
                Motivations = new MotivationState()
            });
            return engine;
        }

        private IHumanContext BuildCtx(
            bool hasPrivacy, SurfaceKind surfaceKind,
            double? proxemicDistance = null,
            double neuroticism = 0.5, double extraversion = 0.5)
        {
            var physio = new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, 20, 10, DiscreteEmotion.Neutral,
                Motivations: new MotivationState());
            var personality = new Personality(
                new BigFive(0.5, 0.5, extraversion, 0.5, neuroticism),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);
            var surface = new InteractionSurface(
                Location: hasPrivacy ? "private-room" : "plaza",
                HasPrivacy: hasPrivacy,
                Noise: 0.2,
                Crowding: 0.3,
                Kind: surfaceKind,
                ProxemicDistanceMeters: proxemicDistance);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                surface,
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()), Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildMinCtx(HumanId id)
        {
            var physio = new PhysiologyState(80, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("loc", false, 0.2, 0.3, SurfaceKind.Social),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = id, Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildMinCtxWithStress(HumanId id, double stress, IRandomSource random)
        {
            var physio = new PhysiologyState(80, 0, 5, 5, 0, 0, 0, null);
            var psych  = new PsychologyState(0.0, 0.4, 0.5, stress, 10, DiscreteEmotion.Neutral);
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5), AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate, Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface("loc", false, 0.2, 0.3, SurfaceKind.Social),
                new RelationshipState(new System.Collections.Generic.Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new System.Collections.Generic.List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = id, Biology = SexBiology.Female,
                Personality = personality, PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot, Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(), Scheduler = new NullScheduler()
            };
        }

        private sealed class ZeroRandom : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => false;
        }

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;
            public SeededRandom(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent e) { }
            public IDisposable Subscribe<T>(Action<T> h) where T : class, IDomainEvent => new D();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime w, ScheduledAction a, string? t = null) => new(Guid.NewGuid());
            public ScheduledId ScheduleAfter(WDateTime n, WTimeSpan d, ScheduledAction a, string? t = null) => new(Guid.NewGuid());
            public bool Cancel(ScheduledId id) => true;
            public System.Collections.Generic.IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n)
                => System.Linq.Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class D : IDisposable { public void Dispose() { } }
    }
}
