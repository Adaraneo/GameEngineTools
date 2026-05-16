// CharacterDevelopmentTests.cs
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
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GenProjector = GameEngineTools.Characters.Generation.AppearanceProjector;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class CharacterDevelopmentTests : TestBase
    {
        [TestMethod]
        public void DevelopmentPolicy_GatesAdultActionsByStadium()
        {
            var policy = new DefaultCharacterDevelopmentPolicy();

            Assert.IsFalse(policy.AllowsAction(StadiumType.Baby, Work));
            Assert.IsFalse(policy.AllowsAction(StadiumType.Baby, ReachOut));
            Assert.IsTrue(policy.AllowsAction(StadiumType.Baby, Eat));

            Assert.IsFalse(policy.AllowsAction(StadiumType.Child, InviteIntimacy));
            Assert.IsTrue(policy.AllowsAction(StadiumType.Child, ReachOut));

            Assert.IsFalse(policy.AllowsAction(StadiumType.Teenager, InviteIntimacy));
            Assert.IsTrue(policy.AllowsAction(StadiumType.Adult, InviteIntimacy));
        }

        [TestMethod]
        public void OrchestratedHuman_ReportsRuntimeBabyStadiumFromBirthDate()
        {
            var generator = ServiceProvider.GetRequiredService<IChildBlueprintGenerator>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var bornOn = ServiceProvider.GetRequiredService<IClock>().Now.Date;

            var child = factory.Create(generator.Generate(
                Parent(SexBiology.Female, 165, HairColorNatural.Brown),
                Parent(SexBiology.Male, 185, HairColorNatural.Black),
                bornOn,
                seed: 1234));

            Assert.AreEqual(0, child.Age);
            Assert.AreEqual(StadiumType.Baby, child.Stadium);
            Assert.IsNull(child.AttractionProfile);
            Assert.AreEqual(Sociosexuality.Restricted, child.Personality.Sociosexuality);
            Assert.AreEqual(0.0, child.Personality.Motivation.Sexuality, 0.0001);
        }

        [TestMethod]
        public void ChildBlueprintGenerator_SameSeedProducesSameInheritedTraits()
        {
            var generator = ServiceProvider.GetRequiredService<IChildBlueprintGenerator>();
            var mother = Parent(SexBiology.Female, 162, HairColorNatural.Red);
            var father = Parent(SexBiology.Male, 188, HairColorNatural.Black);
            var bornOn = WDateOnly.New(100, 1, 1);

            var a = generator.Generate(mother, father, bornOn, seed: 42);
            var b = generator.Generate(mother, father, bornOn, seed: 42);

            Assert.AreEqual(a.Biology, b.Biology);
            Assert.AreEqual(a.Identity.BirthDate, b.Identity.BirthDate);
            Assert.AreEqual(mother.Identity.LastName, a.Identity.LastName);
            Assert.AreEqual(a.GeneticBlueprint, b.GeneticBlueprint);
            Assert.AreEqual(a.Personality, b.Personality);
            Assert.IsTrue(GenProjector.Project(a.GeneticBlueprint, ageYears: 0.5).Body.Proportions.HeightCm is >= 45 and <= 95);
        }

        [TestMethod]
        public void BehaviorEngine_BabyDoesNotCommitWorkOrIntimacy()
        {
            var engine = new DefaultBehaviorEngine(
                Options.Create(new BehaviorConfig()),
                Options.Create(new SleepConfig()),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)),
                new DefaultCharacterDevelopmentPolicy());
            var ctx = BabyContext();
            var outbox = new EventCollector();

            engine.Tick(WDateTime.New(100, 1, 1), WTimeSpan.FromHours(1), ctx, outbox);

            var committed = outbox.Drain().OfType<ActionCommitted>().ToList();
            Assert.IsFalse(committed.Any(ev => ev.ActionName is Work or Create or InviteIntimacy or ReachOut));
        }

        private static IHumanContext BabyContext()
        {
            var self = new HumanId(Guid.NewGuid());
            var target = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                new BigFive(0.7, 0.1, 0.5, 0.6, 0.8),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(1.0, 1.0, 0.2, 0.4, 1.0, 0.5, 1.0, 1.0, 1.0),
                Sociosexuality.Restricted,
                Chronotype.Neutral);
            var snapshot = new EnginesSnapshot(
                new PhysiologyState(95, 0, 5, 5, 0, 0, 0, null),
                new PsychologyState(0.1, 0.5, 0.5, 5, 0, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 90, 95, 90, null),
                new InteractionSurface("private", true, 0.1, 0.1, SurfaceKind.Private),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>
                {
                    [target] = new(
                        self,
                        target,
                        Like: 80,
                        Trust: 80,
                        Familiarity: 80,
                        AestheticAttraction: 80,
                        PhysicalAttraction: 80,
                        IntimateAffinity: 80,
                        SexualInterest: 80,
                        Closeness: 80,
                        Respect: 80,
                        Comfort: 80,
                        Breakdown: new DomainBreakdown(60, 60, 60, 60, 80))
                }),
                new MemoryIndex(new List<EpisodicMemory>()),
                SemanticMemoryState.Empty);

            return new HumanContext
            {
                Id = self,
                Identity = new Identity(
                    new Name { Original = "Baby", Familiar = new[] { "Baby" } },
                    new Surname { Male = "Test", Female = "Test" },
                    WDateOnly.New(100, 1, 1)),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new NeverConflictRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new LocalNullEventBus(),
                Scheduler = new LocalNullScheduler()
            };
        }

        private static IHuman Parent(SexBiology biology, double height, HairColorNatural hairColor)
        {
            var id = new HumanId(Guid.NewGuid());
            var personality = new Personality(
                new BigFive(0.55, 0.60, 0.45, 0.65, 0.35),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.6, 0.5, 0.3, 0.4, 0.5, 0.5, 0.4, 0.6, 0.5),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            return new LocalHuman(
                id,
                new Identity(
                    new Name { Original = biology == SexBiology.Female ? "ParentA" : "ParentB", Familiar = new[] { "Parent" } },
                    new Surname { Male = "Inherited", Female = "Inherited" },
                    WDateOnly.New(70, 1, 1)),
                biology,
                personality,
                TestAppearanceFactory.Build(
                    heightCm: height,
                    frame: BodyFrame.Medium,
                    skinTone: SkinTone.Light,
                    eyeColor: EyeColor.Brown,
                    hairColor: hairColor,
                    hairType: HairType.Wavy,
                    faceShape: FaceShape.Oval,
                    shoulderBreadthCm: 42,
                    hipBreadthCm: 38,
                    noseProjection: 0.5,
                    lipFullness: 0.55));
        }

        private sealed class LocalHuman : IHuman
        {
            public LocalHuman(HumanId id, Identity identity, SexBiology biology, Personality personality, PhysicalAppearance appearance)
            {
                Id = id;
                Identity = identity;
                Biology = biology;
                Personality = personality;
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality);
                PhysicalAppearance = appearance;
            }

            public HumanId Id { get; }
            public Identity Identity { get; }
            public SexBiology Biology { get; }
            public Personality Personality { get; }
            public PsychologicalProfile PsychologyProfile { get; }
            public PhysicalAppearance PhysicalAppearance { get; }
            public AttractionProfile AttractionProfile => null!;
            public EnginesSnapshot Snapshot => default!;
            public IReadOnlyList<IDomainEvent> LastOutbox => Array.Empty<IDomainEvent>();
            public int Age => 30;
            public StadiumType Stadium => StadiumType.Adult;
            public void Tick(WDateTime now, WTimeSpan dt) { }
            public void ReceiveEvent(IDomainEvent @event) { }
            public void RestoreSnapshot(EnginesSnapshot snapshot) { }
            public void FlushInbox() { }
        }

        private sealed class NeverConflictRandom : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit() => 0;
            public bool Chance(double p) => false;
        }
    }
}
