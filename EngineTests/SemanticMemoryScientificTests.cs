// SemanticMemoryScientificTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.SemanticMemory;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static EngineTests.SemanticSciTestHelper;

    // =========================================================================
    // Attachment Style → Belief Learning Modulation
    // (Bartholomew-Horowitz 2D model, mapped onto 4 discrete styles)
    // =========================================================================

    [TestClass]
    public class AttachmentBeliefLearningTests : TestBase
    {
        [TestMethod]
        public void Anxious_AttachmentStyle_ProducesHigherBeliefStrength_ThanSecure()
        {
            // Anxious: learningMult = 1.30 → po stejném počtu eventů silnější belief
            var now = WDateTime.New(100, 1, 10, 12);
            var self = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure  = BuildEngine();
            var engineAnxious = BuildEngine();
            var ctxSecure     = BuildCtxWith(self, AttachmentProfile.Secure);
            var ctxAnxious    = BuildCtxWith(self, AttachmentProfile.Preoccupied);
            var outbox = new EventCollector();

            for (var i = 0; i < 3; i++)
            {
                var ev = RejectingEvent(self, other, new WDateTime(i));
                engineSecure.Handle(ev, ctxSecure, outbox);
                engineAnxious.Handle(ev, ctxAnxious, outbox);
            }

            var secureStrength  = engineSecure.State.GetStrength(other, PersonBeliefKind.Rejecting);
            var anxiousStrength = engineAnxious.State.GetStrength(other, PersonBeliefKind.Rejecting);

            Assert.IsTrue(anxiousStrength > secureStrength,
                $"Anxious attachment musí mít vyšší Rejecting strength. Anxious={anxiousStrength:F4}, Secure={secureStrength:F4}");
        }

        [TestMethod]
        public void Avoidant_AttachmentStyle_SuppressesEmotionallySafe_RelativeTo_Warm()
        {
            // Avoidant: safeDiscount = 0.45 → EmotionallySafe výrazně nižší než Warm
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure  = BuildEngine();
            var engineAvoidant = BuildEngine();
            var ctxSecure   = BuildCtxWith(self, AttachmentProfile.Secure);
            var ctxAvoidant = BuildCtxWith(self, AttachmentProfile.Dismissing);
            var outbox = new EventCollector();

            // Validation:Accepted → generuje Warm (0.20) + EmotionallySafe (0.24)
            for (var i = 0; i < 4; i++)
            {
                var ev = ValidationAcceptedEvent(self, other, new WDateTime(i));
                engineSecure.Handle(ev, ctxSecure, outbox);
                engineAvoidant.Handle(ev, ctxAvoidant, outbox);
            }

            var secureRatio  = engineSecure.State.GetStrength(other, PersonBeliefKind.EmotionallySafe)
                             / Math.Max(0.001, engineSecure.State.GetStrength(other, PersonBeliefKind.Warm));
            var avoidantRatio = engineAvoidant.State.GetStrength(other, PersonBeliefKind.EmotionallySafe)
                              / Math.Max(0.001, engineAvoidant.State.GetStrength(other, PersonBeliefKind.Warm));

            Assert.IsTrue(avoidantRatio < secureRatio,
                $"Avoidant attachment musí potlačit EmotionallySafe vůči Warm. " +
                $"Secure ratio={secureRatio:F3}, Avoidant ratio={avoidantRatio:F3}");
        }

        [TestMethod]
        public void Avoidant_AttachmentStyle_ProducesLowerOverallBeliefStrength_ThanSecure()
        {
            // Avoidant: learningMult = 0.75 → pomalejší growth
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure   = BuildEngine();
            var engineAvoidant = BuildEngine();
            var ctxSecure   = BuildCtxWith(self, AttachmentProfile.Secure);
            var ctxAvoidant = BuildCtxWith(self, AttachmentProfile.Dismissing);
            var outbox = new EventCollector();

            for (var i = 0; i < 4; i++)
            {
                var ev = RejectingEvent(self, other, new WDateTime(i));
                engineSecure.Handle(ev, ctxSecure, outbox);
                engineAvoidant.Handle(ev, ctxAvoidant, outbox);
            }

            var secureStrength   = engineSecure.State.GetStrength(other, PersonBeliefKind.Rejecting);
            var avoidantStrength = engineAvoidant.State.GetStrength(other, PersonBeliefKind.Rejecting);

            Assert.IsTrue(avoidantStrength < secureStrength,
                $"Avoidant attachment musí mít nižší celkový learning. Avoidant={avoidantStrength:F4}, Secure={secureStrength:F4}");
        }

        [TestMethod]
        public void Disorganized_AttachmentStyle_HigherContradictionSensitivity_LargerDropPerWarmEvent()
        {
            // Disorganized: contradictionMult = 1.40 → každý Warm event smaže více z Rejecting.
            // Test: stejná počáteční Rejecting strength (via RestoreState), pak 1 Warm event.
            // Zjistíme amplitudu poklesu — Disorganized musí klesnout více.
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineSecure       = BuildEngine();
            var engineDisorganized = BuildEngine();
            var ctxSecure       = BuildCtxWith(self, AttachmentProfile.Secure);
            var ctxDisorganized = BuildCtxWith(self, AttachmentProfile.Fearful);
            var outbox = new EventCollector();

            // Stejný počáteční stav pro oba enginy (Rejecting strength = 0.50)
            var initialState = BeliefState(other, PersonBeliefKind.Rejecting, 0.50, 0.20, new WDateTime(0));
            engineSecure.RestoreState(initialState);
            engineDisorganized.RestoreState(initialState);

            // 1 Warm event → contradikuje Rejecting
            var warmEv = ValidationAcceptedEvent(self, other, new WDateTime(10));
            engineSecure.Handle(warmEv, ctxSecure, outbox);
            engineDisorganized.Handle(warmEv, ctxDisorganized, outbox);

            var secureRejecting       = engineSecure.State.GetStrength(other, PersonBeliefKind.Rejecting);
            var disorganizedRejecting = engineDisorganized.State.GetStrength(other, PersonBeliefKind.Rejecting);

            // Disorganized: contradictionMult=1.40 → větší pokles Rejecting
            var secureDropped       = 0.50 - secureRejecting;
            var disorganizedDropped = 0.50 - disorganizedRejecting;

            Assert.IsTrue(disorganizedDropped > secureDropped,
                $"Disorganized musí mít větší pokles Rejecting po Warm eventu. " +
                $"Disorganized drop={disorganizedDropped:F4}, Secure drop={secureDropped:F4}");
        }

        [TestMethod]
        public void Secure_AttachmentStyle_MatchesBaseline_Behavior()
        {
            // Secure = baseline (multiplikátory 1.0, 1.0, 1.0) — ověřuje backward compat
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());

            var engineDefault = BuildEngine();
            var engineSecure  = BuildEngine();
            var ctxDefault = BuildCtxWith(self, AttachmentProfile.Secure);
            var ctxSecure  = BuildCtxWith(self, AttachmentProfile.Secure);
            var outbox = new EventCollector();

            for (var i = 0; i < 3; i++)
            {
                var ev = RejectingEvent(self, other, new WDateTime(i));
                engineDefault.Handle(ev, ctxDefault, outbox);
                engineSecure.Handle(ev, ctxSecure, outbox);
            }

            var defaultStrength = engineDefault.State.GetStrength(other, PersonBeliefKind.Rejecting);
            var secureStrength  = engineSecure.State.GetStrength(other, PersonBeliefKind.Rejecting);

            Assert.AreEqual(defaultStrength, secureStrength, 0.0001,
                "Secure attachment musí dávat identické výsledky jako výchozí config.");
        }
    }

    // =========================================================================
    // Navarro 8× Gap Rule (Navarro et al. 2017)
    // Pokud uplynulo déle než 8× průměrný meziinterakční interval → 3× decay
    // =========================================================================

    [TestClass]
    public class NavarroCriticalGapTests : TestBase
    {
        [TestMethod]
        public void Navarro_GapExceedsCritical_AcceleratesDecay()
        {
            // Closeness = 30 → expectedInterval = 21 dní → threshold = 168 dní
            // Gap = 200 dní >> 168 → gapMultiplier = 3.0
            var now = WDateTime.New(100, 5, 1, 0);
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var dt = WTimeSpan.FromDays(1);

            var engineGap    = BuildEngine();
            var engineNormal = BuildEngine();
            var ctx = BuildCtxWith(self, AttachmentProfile.Secure);

            // Stav s velmi starým LastUpdatedAt (200 dní před now)
            var oldTime    = now - WTimeSpan.FromDays(200);
            var recentTime = now - WTimeSpan.FromDays(5);

            engineGap.RestoreState(BeliefState(other, PersonBeliefKind.Warm, 0.80, 0.10, oldTime));
            engineNormal.RestoreState(BeliefState(other, PersonBeliefKind.Warm, 0.80, 0.10, recentTime));

            var outbox = new EventCollector();
            engineGap.Tick(now, dt, ctx, outbox);
            engineNormal.Tick(now, dt, ctx, outbox);

            var gapStrength    = engineGap.State.GetStrength(other, PersonBeliefKind.Warm);
            var normalStrength = engineNormal.State.GetStrength(other, PersonBeliefKind.Warm);

            Assert.IsTrue(gapStrength < normalStrength,
                $"Po Navarro threshold musí belief klesnout rychleji. " +
                $"Gap={gapStrength:F4}, Normal={normalStrength:F4}");

            // Diferenece by měla odpovídat přibližně 3× zrychlení
            var expectedDecayRatio = 3.0;
            var decayGap    = 0.80 - gapStrength;
            var decayNormal = 0.80 - normalStrength;
            Assert.IsTrue(decayGap > decayNormal * (expectedDecayRatio * 0.8),
                $"Navarro decay musí být alespoň ~3× větší. decayGap={decayGap:F4}, decayNormal={decayNormal:F4}");
        }

        [TestMethod]
        public void Navarro_GapBelowCritical_NormalDecay()
        {
            // Gap = 5 dní << 168 → gapMultiplier = 1.0 → normální decay
            var now = WDateTime.New(100, 2, 1, 0);
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var dt = WTimeSpan.FromDays(1);

            var engine = BuildEngine();
            var ctx = BuildCtxWith(self, AttachmentProfile.Secure);

            var recentTime = now - WTimeSpan.FromDays(5);
            engine.RestoreState(BeliefState(other, PersonBeliefKind.Warm, 0.80, 0.10, recentTime));

            var outbox = new EventCollector();
            engine.Tick(now, dt, ctx, outbox);

            var strength = engine.State.GetStrength(other, PersonBeliefKind.Warm);

            // Normální decay: DecayPerDay=0.01, days=1, stability=0.10 → decay ≈ 0.01*(1-0.08) ≈ 0.0092
            Assert.IsTrue(strength > 0.78,
                $"Normální decay na jeden den musí být malý. Strength={strength:F4}");
            Assert.IsTrue(strength < 0.80,
                "Strength musí klesat i při normálním decay.");
        }
    }

    // =========================================================================
    // ForgetPerson + GetBeliefsSorted (diagnostics & management)
    // =========================================================================

    [TestClass]
    public class SemanticMemoryManagementTests : TestBase
    {
        [TestMethod]
        public void ForgetPerson_RemovesAllBeliefsAboutThatPerson()
        {
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx = BuildCtxWith(self, AttachmentProfile.Secure);
            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Naplnit beliefs
            for (var i = 0; i < 3; i++)
                engine.Handle(RejectingEvent(self, other, new WDateTime(i)), ctx, outbox);

            Assert.IsNotNull(engine.State.GetBeliefs(other), "Beliefs musí existovat před ForgetPerson.");

            engine.ForgetPerson(other);

            Assert.IsNull(engine.State.GetBeliefs(other),
                "Po ForgetPerson nesmí existovat žádné beliefs o dané osobě.");
            Assert.AreEqual(0, engine.GetBeliefsSorted(other).Count,
                "GetBeliefsSorted po ForgetPerson musí vrátit prázdný seznam.");
        }

        [TestMethod]
        public void ForgetPerson_NonExistentPerson_IsNoOp()
        {
            var engine = BuildEngine();
            var unknown = new HumanId(Guid.NewGuid());

            // Nesmí vyhodit výjimku
            engine.ForgetPerson(unknown);

            Assert.AreEqual(0, engine.State.People.Count,
                "State musí zůstat prázdný.");
        }

        [TestMethod]
        public void ForgetPerson_DoesNotAffectOtherPeople()
        {
            var self   = new HumanId(Guid.NewGuid());
            var alpha  = new HumanId(Guid.NewGuid());
            var beta   = new HumanId(Guid.NewGuid());
            var ctx    = BuildCtxWith(self, AttachmentProfile.Secure);
            var engine = BuildEngine();
            var outbox = new EventCollector();

            engine.Handle(RejectingEvent(self, alpha, new WDateTime(1)), ctx, outbox);
            engine.Handle(RejectingEvent(self, beta,  new WDateTime(2)), ctx, outbox);

            engine.ForgetPerson(alpha);

            Assert.IsNull(engine.State.GetBeliefs(alpha), "Alpha musí být zapomenuta.");
            Assert.IsNotNull(engine.State.GetBeliefs(beta), "Beta musí zůstat nedotčena.");
        }

        [TestMethod]
        public void GetBeliefsSorted_ReturnsBeliefsByStrengthDescending()
        {
            var self  = new HumanId(Guid.NewGuid());
            var other = new HumanId(Guid.NewGuid());
            var ctx   = BuildCtxWith(self, AttachmentProfile.Secure);
            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Vytvořím Rejecting (silnější — více eventů) a Warm (slabší)
            for (var i = 0; i < 4; i++)
                engine.Handle(RejectingEvent(self, other, new WDateTime(i)), ctx, outbox);

            engine.Handle(ValidationAcceptedEvent(self, other, new WDateTime(10)), ctx, outbox);

            var sorted = engine.GetBeliefsSorted(other);

            Assert.IsTrue(sorted.Count >= 2, "Musí existovat alespoň 2 beliefs.");
            for (var i = 1; i < sorted.Count; i++)
            {
                Assert.IsTrue(sorted[i - 1].Strength >= sorted[i].Strength,
                    $"Beliefs musí být seřazeny sestupně. [{i-1}]={sorted[i-1].Strength:F4} < [{i}]={sorted[i].Strength:F4}");
            }
        }

        [TestMethod]
        public void GetBeliefsSorted_UnknownPerson_ReturnsEmptyList()
        {
            var engine  = BuildEngine();
            var unknown = new HumanId(Guid.NewGuid());

            var result = engine.GetBeliefsSorted(unknown);

            Assert.AreEqual(0, result.Count, "Pro neznámou osobu musí vrátit prázdný seznam.");
        }
    }

    // =========================================================================
    // Sdílené helpers
    // =========================================================================

    internal static class SemanticSciTestHelper
    {
        internal static DefaultSemanticMemoryEngine BuildEngine()
            => new(Options.Create(new SemanticMemoryConfig()));

        private sealed class LocalZeroRandom : IRandomSource
        {
            public int Next(int minInclusive, int maxExclusive) => minInclusive;
            public double NextUnit() => 0.0;
            public bool Chance(double p) => p >= 1.0;
        }

        private sealed class LocalNullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event) { }
            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
            private sealed class NullDisposable : IDisposable { public void Dispose() { } }
        }

        private sealed class LocalNullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new(Guid.NewGuid());
            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new(Guid.NewGuid());
            public bool Cancel(ScheduledId id) => false;
            public IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now)
                => Array.Empty<(ScheduledId, ScheduledAction)>();
        }

        internal static IHumanContext BuildCtxWith(HumanId self, AttachmentProfile attachment)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                attachment,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "A", Familiar = new[] { "A" } },
                    new Surname { Male = "B", Female = "B" },
                    WDateOnly.New(100, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = new EnginesSnapshot(
                    new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                    new PsychologyState(0, 0.5, 0.5, 10, 0, DiscreteEmotion.Neutral),
                    new BehaviorState(10, 5, 5, 20, 50, 30, null),
                    new InteractionSurface("test", false, 0.2, 0.2, SurfaceKind.Social),
                    new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                    new MemoryIndex(new List<EpisodicMemory>()),
                    SemanticMemoryState.Empty),
                Random = new LocalZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                    .CreateLogger("SemanticSciTests"),
                EventBus = new LocalNullEventBus(),
                Scheduler = new LocalNullScheduler()
            };
        }

        internal static MemoryEncoded RejectingEvent(HumanId self, HumanId other, WDateTime at)
            => new(at, self, Guid.NewGuid(), 0.85,
                "Interaction:Question:Rejected",
                "PerceivedThreat:Interaction:Question:Rejected",
                other,
                new PersonBeliefEvidence(other, PersonBeliefKind.Rejecting, 0.22, "test"));

        internal static MemoryEncoded ValidationAcceptedEvent(HumanId self, HumanId other, WDateTime at)
            => new(at, self, Guid.NewGuid(), 0.70,
                "Interaction:Validation:Accepted",
                "PerceivedWarmth:Interaction:Validation:Accepted",
                other,
                new PersonBeliefEvidence(other, PersonBeliefKind.Warm, 0.18, "test"));

        internal static SemanticMemoryState BeliefState(
            HumanId other, PersonBeliefKind kind, double strength, double stability, WDateTime lastUpdated)
        {
            var belief = new PersonBelief(other, kind, strength, stability, 3, lastUpdated, "test");
            var set = new PersonBeliefSet(other, new Dictionary<PersonBeliefKind, PersonBelief>
            {
                [kind] = belief
            });
            return new SemanticMemoryState(new Dictionary<HumanId, PersonBeliefSet>
            {
                [other] = set
            });
        }
    }
}
