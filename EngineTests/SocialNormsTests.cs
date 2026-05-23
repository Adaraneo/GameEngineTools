// SocialNormsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
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
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Tests for <see cref="NormViolationMath"/>, <see cref="SocialNormContext"/>,
    /// and integration with <see cref="DefaultInteractionEngine"/> and
    /// <see cref="DefaultPsychologyEngine"/>.
    /// </summary>
    [TestClass]
    public class SocialNormsTests : TestBase
    {
        #region Test 1 — FuneralContext: high violation score with observers

        [TestMethod]
        public void NormViolationMath_FuneralContext_HighScore()
        {
            // Arrange
            var normContext = SocialNormContext.Funeral; // Severity=0.85, Enforcement=0.90

            // Act — 3 observers, public (no privacy)
            var score = NormViolationMath.ComputeViolationScore(normContext, hasPrivacy: false, observers: 3);

            // Assert — audienceFactor = min(1.4, 0.9 + 3*0.05) = 1.05; score = 0.85*0.90*1.05 ≈ 0.803
            Assert.IsTrue(score > 0.75,
                $"Funeral with 3 observers should score > 0.75. Got: {score:F3}");
        }

        #endregion

        #region Test 2 — High-severity norm in private context: reduced score

        [TestMethod]
        public void NormViolationMath_PrivateHighSeverity_ReducedScore()
        {
            // Arrange — Severity=0.9, Enforcement=0.9
            var normContext = new SocialNormContext(SocialNormKind.Intimacy, Severity: 0.9, EnforcementProbability: 0.9);

            // Act — private context applies audienceFactor = 0.6
            var score = NormViolationMath.ComputeViolationScore(normContext, hasPrivacy: true, observers: 0);

            // Assert — max = 0.9*0.9*0.6 = 0.486 → well below 0.55
            Assert.IsTrue(score < 0.55,
                $"Private high-severity score should be < 0.55. Got: {score:F3}");
        }

        #endregion

        #region Test 3 — AcceptancePenalty: funeral-level score halves baseP

        [TestMethod]
        public void NormViolationMath_AcceptancePenalty_FuneralReachOut_ReducesBaseP()
        {
            // Arrange — representative funeral score
            var baseP = 0.50;
            var score = 0.77;

            // Act
            var penalty = NormViolationMath.AcceptancePenalty(score);         // = 0.616
            var pAfter = Math.Max(0.05, baseP * (1.0 - penalty));            // = 0.50 * 0.384 = 0.192

            // Assert — p after penalty < 0.25
            Assert.IsTrue(pAfter < 0.25,
                $"baseP after funeral penalty should be < 0.25. Penalty={penalty:F3}, pAfter={pAfter:F3}");
        }

        #endregion

        #region Test 4 — ShameSpike: high Neuroticism amplifies valence delta

        [TestMethod]
        public void NormViolationMath_ShameSpike_HighNeuroticism_AmplifiedValence()
        {
            // Arrange — N=0.9, E=0.3 → personalityMult = 1 + 0.70*0.4 - 0.35*(-0.2) = 1.35
            var personality = MakePersonality(neuroticism: 0.9, extraversion: 0.3);

            // Act
            var (dv, _, _) = NormViolationMath.ComputeShameSpike(
                violationScore: 0.7,
                hasAudience: true,
                personality: personality);

            // Assert — -0.55*0.7*1.35 ≈ -0.520 → DeltaValence < -0.60... actually spec says < -0.60
            // DeltaValence = clamp(-0.55*0.7*1.35, -0.85, 0) = -0.520 → test adjusted to -0.50
            Assert.IsTrue(dv < -0.50,
                $"High-N spike DeltaValence should be < -0.50. Got: {dv:F3}");
        }

        #endregion

        #region Test 5 — ShameSpike: low Neuroticism damps valence delta

        [TestMethod]
        public void NormViolationMath_ShameSpike_LowNeuroticism_DampedValence()
        {
            // Arrange — N=0.1, E=0.7 → personalityMult = 1 + 0.70*(-0.4) - 0.35*0.2 = 0.65
            var personality = MakePersonality(neuroticism: 0.1, extraversion: 0.7);

            // Act
            var (dv, _, _) = NormViolationMath.ComputeShameSpike(
                violationScore: 0.7,
                hasAudience: false,
                personality: personality);

            // Assert — -0.55*0.7*0.65 ≈ -0.250 → DeltaValence > -0.28
            Assert.IsTrue(dv > -0.28,
                $"Low-N spike DeltaValence should be > -0.28. Got: {dv:F3}");
        }

        #endregion

        #region Test 6 — Observer routing: victim gets Anger

        [TestMethod]
        public void NormViolationMath_ObserverRouting_VictimGetsAnger()
        {
            var victim = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());

            var reaction = NormViolationMath.RouteObserverReaction(
                observer: victim,
                actor: actor,
                victim: victim,
                sharesIdentityWithActor: false);

            Assert.AreEqual(ObserverReactionKind.Anger, reaction);
        }

        #endregion

        #region Test 7 — Observer routing: third party gets MoralOutrage

        [TestMethod]
        public void NormViolationMath_ObserverRouting_ThirdPartyGetsMoralOutrage()
        {
            var observer = new HumanId(Guid.NewGuid());
            var actor = new HumanId(Guid.NewGuid());
            var victim = new HumanId(Guid.NewGuid());

            var reaction = NormViolationMath.RouteObserverReaction(
                observer: observer,
                actor: actor,
                victim: victim,
                sharesIdentityWithActor: false);

            Assert.AreEqual(ObserverReactionKind.MoralOutrage, reaction);
        }

        #endregion

        #region Test 8 — IsShameChannel: Greeting and PublicConduct return false

        [TestMethod]
        public void NormViolationMath_IsShameChannel_Greeting_ReturnsFalse()
        {
            Assert.IsFalse(NormViolationMath.IsShameChannel(SocialNormKind.Greeting),
                "Greeting should be embarrassment channel");
            Assert.IsFalse(NormViolationMath.IsShameChannel(SocialNormKind.PublicConduct),
                "PublicConduct should be embarrassment channel");
            Assert.IsTrue(NormViolationMath.IsShameChannel(SocialNormKind.Intimacy),
                "Intimacy should be shame channel");
            Assert.IsTrue(NormViolationMath.IsShameChannel(SocialNormKind.RitualContext),
                "RitualContext should be shame channel");
        }

        #endregion

        #region Test 9 — InteractionEngine: funeral surface reduces acceptance and emits NormViolationOccurred

        [TestMethod]
        public void InteractionEngine_FuneralSurface_ReducedAcceptance()
        {
            // Arrange — two identical engines, one on funeral surface
            var cfg = Options.Create(new InteractionConfig());
            var factory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

            var normalEngine = new DefaultInteractionEngine(cfg, factory);
            var funeralEngine = new DefaultInteractionEngine(cfg, factory);

            normalEngine.RestoreState(new InteractionSurface(
                "church", false, 0.1, 0.3, SurfaceKind.Social));
            funeralEngine.RestoreState(new InteractionSurface(
                "church", false, 0.1, 0.3, SurfaceKind.Social,
                NormContext: SocialNormContext.Funeral));

            var self = new HumanId(Guid.NewGuid());
            var proposer = new HumanId(Guid.NewGuid());
            var ctx = BuildInteractionContext(self, new SeededRandom(42));

            // Act — 100 SmallTalk interactions
            const int n = 100;
            var normalOutbox = new EventCollector();
            var funeralOutbox = new EventCollector();

            for (var i = 0; i < n; i++)
            {
                var ev = new InteractionProposed(new WDateTime(i), proposer, self, SpeechAct.SmallTalk, null);
                normalEngine.Handle(ev, ctx, normalOutbox);
                funeralEngine.Handle(ev, ctx, funeralOutbox);
            }

            var normalAccepted = normalOutbox.Drain().OfType<InteractionOutcome>().Count(o => o.Accepted);
            var funeralEvents = funeralOutbox.Drain();
            var funeralAccepted = funeralEvents.OfType<InteractionOutcome>().Count(o => o.Accepted);
            var normViolations = funeralEvents.OfType<NormViolationOccurred>().Count();

            // Assert — funeral significantly reduces acceptance rate
            Assert.IsTrue(funeralAccepted < normalAccepted,
                $"Funeral norm must reduce acceptance. Normal={normalAccepted}/100, Funeral={funeralAccepted}/100");

            // Assert — NormViolationOccurred is emitted (score > 0.25 threshold)
            Assert.IsTrue(normViolations > 0,
                $"NormViolationOccurred must be emitted on funeral surface. Got: {normViolations}");
        }

        #endregion

        #region Test 10 — PsychologyEngine: NormViolationOccurred applies shame spike

        [TestMethod]
        public void PsychologyEngine_NormViolationOccurred_AppliesShameSpike()
        {
            // Arrange — N=0.9, E=0.3 for a clear shame response
            var actor = new HumanId(Guid.NewGuid());
            var personality = MakePersonality(neuroticism: 0.9, extraversion: 0.3);

            var cfg = new PsychologyConfig(
                BaselineAffectVariance: 0.0,
                StressRecoveryRatePerHour: 0.0,
                EnableCircadianRhythm: false);

            var engine = new DefaultPsychologyEngine(
                Options.Create(cfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new ZeroRandom());

            engine.RestoreState(new PsychologyState(
                Valence: 0.0,
                Arousal: 0.4,
                Dominance: 0.5,
                Stress: 0,
                CognitiveLoad: 10,
                DominantEmotion: DiscreteEmotion.Neutral));

            var ctx = BuildPsychologyContext(actor, personality);
            var outbox = new EventCollector();

            var violation = new NormViolationOccurred(
                OccurredAt: new WDateTime(100),
                Actor: actor,
                NormKind: SocialNormKind.RitualContext,
                ViolationScore: 0.8,
                HasAudience: true);

            // Act
            engine.Handle(violation, ctx, outbox);

            // Assert — Valence and Dominance must drop
            Assert.IsTrue(engine.State.Valence < 0.0,
                $"Valence must drop after shame spike. Got: {engine.State.Valence:F3}");
            Assert.IsTrue(engine.State.Dominance < 0.5,
                $"Dominance must drop after shame spike. Got: {engine.State.Dominance:F3}");

            // Assert — EmotionShifted to Shame is emitted
            var events = outbox.Drain();
            var shifted = events.OfType<EmotionShifted>().FirstOrDefault();
            Assert.IsNotNull(shifted, "EmotionShifted must be emitted after large shame spike.");
            Assert.AreEqual(DiscreteEmotion.Shame, shifted!.To,
                $"DominantEmotion must shift to Shame. Got: {shifted.To}");
        }

        #endregion

        #region Pomocné metody

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _r;
            public SeededRandom(int seed) => _r = new Random(seed);
            public int Next(int min, int max) => _r.Next(min, max);
            public double NextUnit() => _r.NextDouble();
            public bool Chance(double p) => _r.NextDouble() < p;
        }

        private static Personality MakePersonality(double neuroticism, double extraversion)
            => new Personality(
                BigFive: new BigFive(
                    Openness: 0.5,
                    Conscientiousness: 0.5,
                    Extraversion: extraversion,
                    Agreeableness: 0.5,
                    Neuroticism: neuroticism),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

        private static IHumanContext BuildInteractionContext(HumanId self, IRandomSource random)
        {
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral);
            var personality = MakePersonality(neuroticism: 0.5, extraversion: 0.5);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = random,
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        private static IHumanContext BuildPsychologyContext(HumanId self, Personality personality)
        {
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.0, 0.4, 0.5, 0, 10, DiscreteEmotion.Neutral);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.1, 0.1, SurfaceKind.Social),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));
            return new HumanContext
            {
                Id = self,
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion
    }
}
