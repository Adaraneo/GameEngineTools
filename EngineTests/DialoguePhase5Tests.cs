// DialoguePhase5Tests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Psychology.Appraisal;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Dialogue.Interpretation;
    using GameEngineTools.Dialogue.Planning;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;

    /// <summary>
    /// Phase 5 — the subjective trace ("unreliable witness"): a listener remembers its OWN divergent
    /// reading of an incoming act, that memory round-trips through serialization, and the full chain
    /// (plan → interpret → appraise) produces divergent emotional consequences for two listeners.
    /// </summary>
    [TestClass]
    public class DialoguePhase5Tests : TestBase
    {
        private static readonly HumanId Speaker = new(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"));

        private IHumanContext Listener(HumanId self, double darkCore)
        {
            var physio = new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null);
            var psych = new PsychologyState(0.1, 0.4, 0.5, 20, 20, DiscreteEmotion.Neutral);
            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.3),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral,
                DarkCore: darkCore > 0 ? new DarkCoreProfile(darkCore, 0.5) : null);
            var snapshot = new EnginesSnapshot(physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.5, 0.5, SurfaceKind.Unknown),
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
                Scheduler = new NullScheduler(),
            };
        }

        private static DefaultMemoryEngine MemoryEngine()
            => new(Options.Create(new MemoryConfig()), LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        private static InteractionProposed IncomingSmallTalk(HumanId to, WDateTime when)
            => InteractionProposed.Of(when, Speaker, to, RelationalActKind.SmallTalk);

        [TestMethod]
        public void PerceivedTrace_HostileListener_StoresPerceivedThreatEpisode()
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = MemoryEngine();
            var ctx = Listener(self, darkCore: 1.0);   // hostile attribution shifts the neutral act to Blunt

            engine.Handle(IncomingSmallTalk(self, WDateTime.New(WDateOnly.New(100, 1, 1))), ctx, new EventCollector());

            var episode = engine.State.Episodes.SingleOrDefault(e => e.PerceivedWhat is not null);
            Assert.IsNotNull(episode, "A divergent reading should leave a subjective trace.");
            StringAssert.StartsWith(episode!.PerceivedWhat!, "PerceivedThreat");
            Assert.AreEqual(EmotionalTag.Negative, episode.Emotion);
            Assert.AreEqual(Speaker, episode.OtherPerson);
        }

        [TestMethod]
        public void PerceivedTrace_PlainListener_StoresNothing()
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = MemoryEngine();
            var ctx = Listener(self, darkCore: 0.0);   // no hostility → act read plainly

            engine.Handle(IncomingSmallTalk(self, WDateTime.New(WDateOnly.New(100, 1, 1))), ctx, new EventCollector());

            Assert.AreEqual(0, engine.State.Episodes.Count, "A plainly-read act must not create a divergent trace.");
        }

        [TestMethod]
        public void PerceivedTrace_RepeatedDivergentActs_GrowthStaysBounded()
        {
            var self = new HumanId(Guid.NewGuid());
            var engine = MemoryEngine();
            var ctx = Listener(self, darkCore: 1.0);

            for (var i = 0; i < 40; i++)
            {
                engine.Handle(IncomingSmallTalk(self, WDateTime.New(WDateOnly.New(100, 1, 1 + i % 20))), ctx, new EventCollector());
            }

            // Same speaker + act kind reinforces one episode (spacing effect) rather than piling up.
            Assert.IsTrue(engine.State.Episodes.Count <= 3, $"Expected bounded growth, got {engine.State.Episodes.Count} episodes.");
        }

        [TestMethod]
        public void Roundtrip_PerceivedEpisode_SurvivesSerialization()
        {
            var options = new JsonSerializerOptions
            {
                Converters =
                {
                    new WDateTimeJsonConverter(),
                    new HumanIdJsonConverter(),
                    new WTimeSpanJsonConverter(),
                },
            };
            var episodes = new List<EpisodicMemory>
            {
                new(Guid.NewGuid(), WDateTime.New(WDateOnly.New(100, 1, 1)),
                    "Interaction:SmallTalk:Heard|from=abcd", 0.5, EmotionalTag.Negative, 0.5,
                    PerceivedWhat: "PerceivedThreat:SmallTalk", OtherPerson: Speaker),
            };

            var json = JsonSerializer.Serialize(episodes, options);
            var restored = JsonSerializer.Deserialize<List<EpisodicMemory>>(json, options)!;

            Assert.AreEqual(1, restored.Count);
            Assert.AreEqual("PerceivedThreat:SmallTalk", restored[0].PerceivedWhat);
            Assert.AreEqual(Speaker, restored[0].OtherPerson);
        }

        [TestMethod]
        public void EndToEnd_PlannedAct_TwoListeners_DivergentAppraisal()
        {
            // event → plan → deliver → interpret at two listeners → two emotional consequences.
            var planner = new DefaultSpeechActPlanner();
            var request = new SpeechActRequest(
                RelationalActKind.SmallTalk,
                EntityRef.ForHuman(Speaker, "Petr"),
                EntityRef.ForHuman(new HumanId(Guid.NewGuid()), "Jana"),
                new WDateTime(1000),
                Closeness: 40, Familiarity: 40,
                Agreeableness: 0.8, Style: CommunicationStyle.Direct, Power: 0.5); // → Directness Neutral
            var act = planner.Plan(request);
            Assert.AreEqual(Directness.Neutral, act.Directness);

            var interpreter = new DefaultSpeechActInterpreter();
            var current = new PsychologyState(0.0, 0.3, 0.5, 20, 10, DiscreteEmotion.Neutral);

            var trusting = interpreter.Appraise(act, new ListenerContext(4, 40, 0.0));
            var hostile = interpreter.Appraise(act, new ListenerContext(4, 40, 0.9));

            var trustingOutcome = PerceivedActAppraiser.ToAppraisal(trusting, 40, current);
            var hostileOutcome = PerceivedActAppraiser.ToAppraisal(hostile, 40, current);

            Assert.IsNull(trustingOutcome, "Trusting listener reads it plainly → no emotional divergence.");
            Assert.IsNotNull(hostileOutcome, "Hostile listener feels it as harsher.");
            Assert.IsTrue(hostileOutcome!.IntrinsicPleasantness < 0);
        }

        [TestMethod]
        public void Stability_ManyListenersOverManyDays_NoErrors_BoundedMemory()
        {
            const int ListenerCount = 12;
            const int SpeakerCount = 6;
            const int Days = 100;
            const int ActsPerDay = 6;

            var rng = new Random(1234);                 // fixed seed → deterministic run
            var planner = new DefaultSpeechActPlanner();
            var kinds = Enum.GetValues<RelationalActKind>();

            var listeners = new List<(HumanId Id, DefaultMemoryEngine Memory, IHumanContext Ctx)>();
            for (var i = 0; i < ListenerCount; i++)
            {
                var id = new HumanId(Guid.NewGuid());
                var darkCore = i % 3 == 0 ? 1.0 : 0.0;  // a third are hostile → produce divergent traces
                listeners.Add((id, MemoryEngine(), Listener(id, darkCore)));
            }

            var speakers = Enumerable.Range(0, SpeakerCount).Select(_ => new HumanId(Guid.NewGuid())).ToList();
            var startTicks = WDateTime.New(WDateOnly.New(100, 1, 1)).WorldTicks;
            var outbox = new EventCollector();
            long step = 0;

            for (var day = 0; day < Days; day++)
            {
                for (var a = 0; a < ActsPerDay; a++)
                {
                    var when = new WDateTime(startTicks + step++);
                    var listener = listeners[rng.Next(ListenerCount)];
                    var speaker = speakers[rng.Next(SpeakerCount)];
                    var kind = kinds[rng.Next(kinds.Length)];

                    // Agreeableness 0.8 + Direct style + power 0.5 → Neutral directness, so a hostile
                    // listener's shift to Blunt is a genuine divergence (exercises the trace path).
                    var request = new SpeechActRequest(
                        kind,
                        EntityRef.ForHuman(speaker, "S"),
                        EntityRef.ForHuman(listener.Id, "L"),
                        when,
                        Closeness: 40, Familiarity: 40,
                        Agreeableness: 0.8, Style: CommunicationStyle.Direct, Power: 0.5);
                    var proposed = new InteractionProposed(when, speaker, listener.Id, new InteractionContent(planner.Plan(request)));

                    listener.Memory.Handle(proposed, listener.Ctx, outbox);
                    outbox.Drain();
                }

                // Advance memory (Ebbinghaus decay / pruning) once per simulated day.
                foreach (var l in listeners)
                {
                    l.Memory.Tick(new WDateTime(startTicks + step++), WTimeSpan.FromDays(1), l.Ctx, outbox);
                    outbox.Drain();
                }
            }

            // Reinforcement keys growth per (speaker, act kind) — so a 100-day run cannot grow unbounded.
            var cap = SpeakerCount * kinds.Length;
            foreach (var l in listeners)
            {
                Assert.IsTrue(l.Memory.State.Episodes.Count <= cap,
                    $"Memory grew unbounded: {l.Memory.State.Episodes.Count} episodes (cap {cap}).");
                Assert.IsTrue(l.Memory.State.Episodes.All(e => e.Strength is >= 0.0 and <= 1.0),
                    "Episode strength left the valid range during the run.");
            }
        }
    }
}
