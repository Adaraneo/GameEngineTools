// ToolLendingTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Needs;
    using GameEngineTools.Characters.Engines.Objects;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using GameEngineTools.World.Work;
    using EngineTests.Utils;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>A tool that belongs to someone else can be used only after its owner has lent it.</summary>
    [TestClass]
    public sealed class ToolLendingTests : TestBase
    {
        private static readonly HumanId Owner = new(Guid.NewGuid());
        private static readonly HumanId Neighbour = new(Guid.NewGuid());
        private static readonly HumanId Stranger = new(Guid.NewGuid());

        private static (SqliteWorldDatabase Db, SqliteWorldObjectProvider Provider) NewWorld()
        {
            var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.Initialize(db);
            db.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('farm', 'Farm', 'Social');");
            var provider = new SqliteWorldObjectProvider(db);
            provider.AddObject(WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm", Owner));
            return (db, provider);
        }

        [TestMethod]
        public void ATool_IsUsableByItsOwnerAndByAnyoneWhenItBelongsToNobody_ButNotByOthers()
        {
            var shared = WorkObjects.Spawn(TestWork.Catalog, "spade", "s", "farm");
            var owned = WorkObjects.Spawn(TestWork.Catalog, "spade", "o", "farm", Owner);

            Assert.IsTrue(WorkObjects.CanUse(shared, Stranger), "Nobody's tool is anyone's.");
            Assert.IsTrue(WorkObjects.CanUse(owned, Owner));
            Assert.IsFalse(WorkObjects.CanUse(owned, Neighbour), "Someone else's tool is not for the taking.");
            Assert.IsFalse(WorkObjects.CanUse(owned, null));
            Assert.IsTrue(WorkObjects.CanUse(owned with { LentTo = Neighbour }, Neighbour), "Once lent, the borrower may use it.");
            Assert.IsFalse(WorkObjects.CanUse(owned with { LentTo = Neighbour }, Stranger), "A loan is to one person.");
        }

        [TestMethod]
        public void ToolsAreCarriable_WorkstationsAreNot()
        {
            Assert.IsTrue(WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm").IsPickable, "A spade can be picked up and carried to the field.");
            Assert.IsFalse(WorkObjects.Spawn(TestWork.Catalog, "mill", "mill_1", "farm").IsPickable);
            Assert.AreEqual("farm", WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm").StoredAt, "A tool belongs where it is first put.");
            Assert.IsNull(WorkObjects.Spawn(TestWork.Catalog, "mill", "mill_1", "farm").StoredAt, "A workstation is not carried back.");
            Assert.IsNull(WorkObjects.CreateItem(TestWork.Catalog, "spade", "smithy", new WDateTime(0), Owner).StoredAt, "A freshly made tool has no shed yet.");
        }

        [TestMethod]
        public void TakingATool_IsRefusedForSomeoneElsesTool_UntilItIsLent()
        {
            var policy = new GameEngineTools.Characters.Engines.Objects.DefaultObjectInteractionPolicy();
            var place = new GameEngineTools.World.Location.LocationDescriptor(
                Id: "farm", DisplayName: "farm", Type: GameEngineTools.World.Location.LocationType.Public, BaseNoise: 0.1, NoisePerPerson: 0.01, Capacity: 20, AllowsPrivacy: false);
            var spade = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm", Owner);
            IHumanContext As(HumanId id) => BehaviorComponentTestFactory.Context(selfId: id).HumanContext;

            Assert.IsTrue(policy.Evaluate(As(Owner), spade, ObjectInteractionKind.Take, place).IsAllowed, "The owner takes their own tool.");
            var refused = policy.Evaluate(As(Neighbour), spade, ObjectInteractionKind.Take, place);
            Assert.IsFalse(refused.IsAllowed, "Someone else's tool is not taken without a loan.");
            Assert.IsTrue(refused.IsSocial);
            Assert.IsTrue(policy.Evaluate(As(Neighbour), spade with { LentTo = Neighbour }, ObjectInteractionKind.Take, place).IsAllowed, "Lent to them: they may take it.");
            Assert.IsTrue(policy.Evaluate(As(Stranger), WorkObjects.Spawn(TestWork.Catalog, "spade", "shared", "farm"), ObjectInteractionKind.Take, place).IsAllowed, "A tool that belongs to nobody is free to take.");
        }

        private static GameEngineTools.World.Objects.WorldMap Roads(params (string From, string To, double Meters)[] roads)
        {
            var adjacency = new Dictionary<string, IReadOnlyList<GameEngineTools.World.Objects.WorldConnection>>();
            foreach (var (from, to, meters) in roads)
            {
                foreach (var (a, b) in new[] { (from, to), (to, from) })
                {
                    if (!adjacency.ContainsKey(a))
                    {
                        adjacency[a] = new List<GameEngineTools.World.Objects.WorldConnection>();
                    }

                    ((List<GameEngineTools.World.Objects.WorldConnection>)adjacency[a]).Add(new GameEngineTools.World.Objects.WorldConnection(b, meters));
                }
            }

            return new GameEngineTools.World.Objects.WorldMap(
                new Dictionary<string, GameEngineTools.World.Location.LocationDescriptor>(), adjacency, new Dictionary<string, IReadOnlyList<string>>());
        }

        private static void AddSickles(IMutableWorldObjectProvider provider)
        {
            for (var i = 2; i <= 6; i++)
            {
                provider.AddObject(WorkObjects.Spawn(TestWork.Catalog, "sickle", $"sickle_{i}", "farm"));
            }

            provider.RemoveObject("farm", "spade_1");
            provider.AddObject(WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", "farm"));
        }

        [TestMethod]
        public void AboutAThirdOfTheTools_BecomePersonal_TheNearestResidentsOwnThem_AndTheRestStayCommon()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                AddSickles(provider);
                var roads = Roads(("farm", "near", 100.0), ("farm", "far", 900.0));

                var assigned = ToolOwnership.AssignPersonalTools(provider, roads, new[] { (Owner, "near"), (Neighbour, "far") });

                var tools = provider.GetAllObjects().Where(o => o.Category == WorldObjectCategory.Tool).ToList();
                Assert.AreEqual(6, tools.Count);
                Assert.AreEqual(2, assigned, "A third of six.");
                Assert.AreEqual(2, tools.Count(o => o.OwnerId is not null));
                Assert.IsTrue(tools.Where(o => o.OwnerId is not null).All(o => o.OwnerId == Owner), "Whoever lives nearest to the farm owns its tools, whatever their job.");
                Assert.AreEqual(0, ToolOwnership.AssignPersonalTools(provider, roads, Array.Empty<(HumanId, string)>()), "Nobody to give them to.");
            }
        }

        [TestMethod]
        public void PeopleSharingTheNearestHome_SplitTheToolsBetweenThem()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                AddSickles(provider);

                ToolOwnership.AssignPersonalTools(provider, Roads(("farm", "flat", 100.0)), new[] { (Owner, "flat"), (Neighbour, "flat") });

                var owners = provider.GetAllObjects().Where(o => o.OwnerId is not null).Select(o => o.OwnerId!.Value).ToArray();
                CollectionAssert.AreEquivalent(new[] { Owner, Neighbour }, owners);
            }
        }

        [TestMethod]
        public void OnlyTheOwnerLends_OnceAtATime_AndEitherSideEndsTheLoan_AndItSurvivesTheDatabase()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Neighbour, Stranger, "farm", Now), "Only the owner can lend.");
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Owner, Owner, "farm", Now), "Lending to oneself is no loan.");
                Assert.IsFalse(ToolLending.Lend(provider, "missing", Owner, Neighbour, "farm", Now));

                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Neighbour, "farm", Now));
                Assert.AreEqual(Neighbour, provider.FindObject("spade_1")!.LentTo, "The loan is kept in the world database.");
                Assert.AreEqual("farm", provider.FindObject("spade_1")!.StoredAt, "The shed is kept in the world database.");
                Assert.AreEqual(Owner, provider.FindObject("spade_1")!.OwnerId, "Lending does not change the owner.");
                Assert.AreEqual("spade_1", provider.GetHeldBy(Neighbour).Single().Id, "The tool is handed over into the borrower's hand.");
                Assert.AreEqual(Neighbour, provider.FindObject("spade_1")!.HeldBy, "A lookup by id shows who holds it, so putting it back in the world does not drop it from the hand.");
                Assert.AreEqual(Neighbour, provider.GetAllObjects().Single(o => o.Id == "spade_1").HeldBy);
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Owner, Stranger, "farm", Now), "A lent tool is not lent twice.");

                Assert.IsFalse(ToolLending.Return(provider, "spade_1", Stranger), "Neither owner nor borrower: no say.");
                Assert.IsTrue(ToolLending.Return(provider, "spade_1", Neighbour));
                Assert.AreEqual(0, provider.GetHeldBy(Neighbour).Count(), "Ending the loan frees the hand.");
                Assert.IsNull(provider.FindObject("spade_1")!.LentTo);
                Assert.IsFalse(ToolLending.Return(provider, "spade_1", Owner), "Nothing to take back.");

                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Stranger, "farm", Now));
                Assert.IsTrue(ToolLending.Return(provider, "spade_1", Owner), "The owner can ask for it back.");
            }
        }

        [TestMethod]
        public void HandBack_PutsTheToolInTheOwnersHand_OnlyFromTheBorrowersHand()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                Assert.IsFalse(ToolLending.HandBack(provider, "spade_1", Neighbour, "farm", Now), "Not lent: nothing to hand back.");
                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Neighbour, "farm", Now));
                Assert.IsFalse(ToolLending.HandBack(provider, "spade_1", Stranger, "farm", Now), "Only the borrower hands it back.");

                Assert.IsTrue(ToolLending.HandBack(provider, "spade_1", Neighbour, "farm", Now));
                var spade = provider.FindObject("spade_1")!;
                Assert.IsNull(spade.LentTo);
                Assert.AreEqual(Owner, spade.HeldBy);
                Assert.AreEqual("farm", spade.LocationId);
                Assert.AreEqual(0, provider.GetHeldBy(Neighbour).Count());
                Assert.AreEqual("spade_1", provider.GetHeldBy(Owner).Single().Id);
                Assert.IsFalse(ToolLending.HandBack(provider, "spade_1", Neighbour, "farm", Now), "The loan is over.");
            }
        }

        [TestMethod]
        public void AHandedBackTool_IsRecognisedInTheOwnersHand_AndSettingItDownFreesIt()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                Assert.IsFalse(ToolLending.IsHandedBack(provider.FindObject("spade_1")!, Owner));
                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Neighbour, "farm", Now));
                Assert.IsFalse(ToolLending.IsHandedBack(provider.FindObject("spade_1")!, Owner), "Still lent.");
                Assert.IsTrue(ToolLending.HandBack(provider, "spade_1", Neighbour, "farm", Now));
                Assert.IsTrue(ToolLending.IsHandedBack(provider.FindObject("spade_1")!, Owner));

                db.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('home', 'Home', 'Private');");
                Assert.IsFalse(ToolLending.SetDown(provider, "spade_1", Stranger, "home"), "Only the holder sets it down.");
                Assert.IsTrue(ToolLending.SetDown(provider, "spade_1", Owner, "home"));
                var spade = provider.FindObject("spade_1")!;
                Assert.IsNull(spade.HeldBy);
                Assert.IsNull(spade.LentAt);
                Assert.AreEqual("home", spade.LocationId);
                Assert.IsFalse(ToolLending.IsHandedBack(spade, Owner));
            }
        }

        [TestMethod]
        public void TheOwnerCanLendATool_HeHoldsHimself_ButNotOneSomeoneElseHolds()
        {
            var (db, provider) = NewWorld();
            using (db)
            {
                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Neighbour, "farm", Now));
                Assert.IsTrue(ToolLending.HandBack(provider, "spade_1", Neighbour, "farm", Now));
                Assert.AreEqual(Owner, provider.FindObject("spade_1")!.HeldBy);

                Assert.IsTrue(ToolLending.Lend(provider, "spade_1", Owner, Stranger, "farm", Now), "A tool in the owner's hand is lent on.");
                var spade = provider.FindObject("spade_1")!;
                Assert.AreEqual(Stranger, spade.LentTo);
                Assert.AreEqual(Stranger, spade.HeldBy);
                Assert.IsFalse(ToolLending.Lend(provider, "spade_1", Owner, Neighbour, "farm", Now), "A tool in the borrower's hand is not lent again.");
            }
        }

        [TestMethod]
        public void FieldWork_NeedsAToolTheCharacterMayUse_AnOwnedSpadeCountsOnlyWhenLent()
        {
            var engine = new FieldWorkNeedEngine(catalog: TestWork.Catalog);
            var self = Neighbour;
            var field = new WorldObject { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemTypeId = "grain", Field = new GameEngineTools.World.Objects.Crops.FieldState(GameEngineTools.World.Objects.Crops.FieldStage.Fallow, 1.0) };
            var spade = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", Loc, Owner);

            BehaviorContext Ctx(WorldObject tool)
            {
                var ctx = BehaviorComponentTestFactory.Context(selfId: self, hunger: 20, thirst: 5, now: new WDateTime(0));
                var human = (HumanContext)ctx.HumanContext;
                human.Snapshot = human.Snapshot with
                {
                    AmbientTemperature = 15.0,
                    Celestial = new GameEngineTools.World.Core.Astro.CelestialContext(0.5, 12.0, 6.0, 18.0, 12.0, 0.5, 0.0, true, 15.0, TemperatureTrendPerDay: 0.1),
                };
                return ctx with { AvailableObjects = new List<WorldObject> { field, tool }, HeldObjects = new List<WorldObject>() };
            }

            Assert.AreEqual(0, engine.Evaluate(Ctx(spade)).Candidates.Count, "The spade belongs to someone else: no ploughing, and a helper off shift does not go asking.");
            Assert.AreEqual(Plough, engine.Evaluate(Ctx(spade with { LentTo = self })).Candidates.Single().Name, "Lent to this character: they plough.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(spade with { LentTo = Stranger })).Candidates.Count, "Lent to somebody else.");
        }

        [TestMethod]
        public void AfterAFailedSearch_TheFarmerDoesNotGoLookingOrAskingAgainUntilTheRetryTime()
        {
            var engine = new FieldWorkNeedEngine(catalog: TestWork.Catalog);
            var field = new WorldObject { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemTypeId = "grain", Field = new GameEngineTools.World.Objects.Crops.FieldState(GameEngineTools.World.Objects.Crops.FieldStage.Fallow, 1.0) };
            var theirs = WorkObjects.Spawn(TestWork.Catalog, "spade", "theirs", Loc, Owner);

            BehaviorContext Ctx(WDateTime now, WDateTime? retryAt)
            {
                var ctx = BehaviorComponentTestFactory.Context(selfId: Neighbour, hunger: 20, thirst: 5, now: now);
                var human = (HumanContext)ctx.HumanContext;
                var slot = new GameEngineTools.Characters.Engines.Schedule.ScheduleSlot("work", 5, Work);
                human.Snapshot = human.Snapshot with
                {
                    AmbientTemperature = 15.0,
                    Celestial = new GameEngineTools.World.Core.Astro.CelestialContext(0.5, 12.0, 6.0, 18.0, 12.0, 0.5, 0.0, true, 15.0, TemperatureTrendPerDay: 0.1),
                    Schedule = new GameEngineTools.Characters.Engines.Schedule.DailyScheduleState(new[] { slot }, slot, 0, "farmer"),
                };
                return ctx with { State = ctx.State with { ToolSearchRetryAt = retryAt }, AvailableObjects = new List<WorldObject> { field, theirs with { LentTo = Stranger } }, HeldObjects = new List<WorldObject>() };
            }

            var start = new WDateTime(0);
            var retry = start + WTimeSpan.FromHours(6);
            Assert.AreEqual(MoveToTool, engine.Evaluate(Ctx(start, null)).Candidates.Single().Name, "Before any failure: go looking.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(start + WTimeSpan.FromHours(1), retry)).Candidates.Count, "The search found nothing a little while ago: not again yet.");
            Assert.AreEqual(MoveToTool, engine.Evaluate(Ctx(retry, retry)).Candidates.Single().Name, "At the retry time the search may be repeated.");
        }

        [TestMethod]
        public void WithoutAToolHere_AFarmerOnShiftAsksTheOwnerOfOneLyingHere_ElseGoesLooking_AHelperDoesNeither()
        {
            var engine = new FieldWorkNeedEngine(catalog: TestWork.Catalog);
            var self = Neighbour;
            var field = new WorldObject { Id = "field_1", DisplayName = "Pole", Category = WorldObjectCategory.Ambient, LocationId = Loc, ItemTypeId = "grain", Field = new GameEngineTools.World.Objects.Crops.FieldState(GameEngineTools.World.Objects.Crops.FieldStage.Fallow, 1.0) };

            BehaviorContext Ctx(bool onShift, params WorldObject[] objects)
            {
                var ctx = BehaviorComponentTestFactory.Context(selfId: self, hunger: 20, thirst: 5, now: new WDateTime(0));
                var human = (HumanContext)ctx.HumanContext;
                human.Snapshot = human.Snapshot with
                {
                    AmbientTemperature = 15.0,
                    Celestial = new GameEngineTools.World.Core.Astro.CelestialContext(0.5, 12.0, 6.0, 18.0, 12.0, 0.5, 0.0, true, 15.0, TemperatureTrendPerDay: 0.1),
                };
                if (onShift)
                {
                    var slot = new GameEngineTools.Characters.Engines.Schedule.ScheduleSlot("work", 5, Work);
                    human.Snapshot = human.Snapshot with { Schedule = new GameEngineTools.Characters.Engines.Schedule.DailyScheduleState(new[] { slot }, slot, 0, "farmer") };
                }

                return ctx with { AvailableObjects = objects.Append(field).ToList(), HeldObjects = new List<WorldObject>() };
            }

            var shared = WorkObjects.Spawn(TestWork.Catalog, "spade", "shared", Loc);
            Assert.AreEqual(Plough, engine.Evaluate(Ctx(true, shared)).Candidates.Single().Name, "A usable spade lying here is already at hand: the job itself.");

            var theirs = WorkObjects.Spawn(TestWork.Catalog, "spade", "theirs", Loc, Owner);
            var ask = engine.Evaluate(Ctx(true, theirs)).Candidates.Single();
            Assert.AreEqual(AskToBorrow, ask.Name, "Someone else's spade lies here, not lent: ask.");
            CollectionAssert.AreEqual(new[] { "spade" }, ask.TargetTypeIds!.ToArray());
            Assert.IsTrue(ask.Duration >= WTimeSpan.FromMinutes(30), "Asking takes a while, so a refusal is not repeated at once.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(false, theirs)).Candidates.Count, "A helper who is not on shift does not go asking.");

            Assert.AreEqual(Plough, engine.Evaluate(Ctx(true, theirs with { LentTo = self })).Candidates.Single().Name, "Lent to them: it is at hand.");

            var elsewhere = engine.Evaluate(Ctx(true, theirs with { LentTo = Stranger })).Candidates.Single();
            Assert.AreEqual(MoveToTool, elsewhere.Name, "Nothing here to ask for or use: go looking.");
            CollectionAssert.AreEqual(new[] { "spade" }, elsewhere.TargetTypeIds!.ToArray(), "Every type that serves as the tool is looked for.");
            Assert.AreEqual(MoveToTool, engine.Evaluate(Ctx(true)).Candidates.Single().Name, "No spade here at all: go looking.");
            Assert.AreEqual(0, engine.Evaluate(Ctx(false)).Candidates.Count, "A helper does not go looking.");
        }

        [TestMethod]
        public void ACarriedTool_IsNotPutDownInTheMiddleOfAShift_AndDroppingALentOneEndsTheLoan()
        {
            var spade = WorkObjects.Spawn(TestWork.Catalog, "spade", "spade_1", Loc, Owner) with { LentTo = Neighbour, HeldBy = Neighbour };

            BehaviorContext Ctx(bool onShift)
            {
                var ctx = BehaviorComponentTestFactory.Context(selfId: Neighbour, hunger: 10, thirst: 5, now: new WDateTime(0));
                if (onShift)
                {
                    var slot = new GameEngineTools.Characters.Engines.Schedule.ScheduleSlot("work", 5, Work);
                    ((HumanContext)ctx.HumanContext).Snapshot = ctx.HumanContext.Snapshot with
                    {
                        Schedule = new GameEngineTools.Characters.Engines.Schedule.DailyScheduleState(new[] { slot }, slot, 0, "farmer"),
                    };
                }

                return ctx with { AvailableObjects = new List<WorldObject>(), HeldObjects = new List<WorldObject> { spade } };
            }

            var provider = new SingleObjectProvider(spade);
            var modifier = new ObjectInteractionBehaviorModifier(provider);
            var off = new List<BehaviorCandidate>();
            var on = new List<BehaviorCandidate>();
            modifier.Modify(Ctx(onShift: false), off);
            modifier.Modify(Ctx(onShift: true), on);

            Assert.IsTrue(off.Any(c => c.ObjectInteraction?.Kind == ObjectInteractionKind.Drop), "Off shift a carried tool may be put down.");
            Assert.IsFalse(on.Any(c => c.ObjectInteraction?.Kind == ObjectInteractionKind.Drop), "On shift it is kept.");
        }

        private sealed class SingleObjectProvider(WorldObject held) : IMutableWorldObjectProvider
        {
            public IEnumerable<WorldObject> GetObjectsAt(string locationId) => Enumerable.Empty<WorldObject>();
            public IEnumerable<WorldObject> GetAllObjects() => new[] { held };
            public IEnumerable<WorldObject> GetAllObjectsAt(string locationId) => Enumerable.Empty<WorldObject>();
            public IEnumerable<WorldObject> GetHeldBy(HumanId holder) => holder == held.HeldBy ? new[] { held } : Enumerable.Empty<WorldObject>();
            public WorldObject? FindObject(string objectId) => objectId == held.Id ? held : null;
            public void AddObject(WorldObject obj) { }
            public bool RemoveObject(string locationId, string objectId) => true;
            public bool SetHeldBy(string locationId, string objectId, HumanId? holder) => true;
            public bool ConsumeObject(string locationId, string objectId, WDateTime at) => true;
            public bool RestoreObject(string locationId, string objectId) => true;
            public IEnumerable<string> GetKnownLocationIds() => Enumerable.Empty<string>();
        }

        private const string Loc = "test"; // BehaviorComponentTestFactory surface location

        private static readonly WDateTime Now = new(0);

        [TestMethod]
        public void TheOwnersChance_RisesWithTrustLikeAndCloseness_FallsWhenHeNeedsTheToolNow_AndAgreeableOwnersLendMoreOften()
        {
            RelationshipEdge Edge(double trust, double like, double closeness)
            {
                return new(Owner, Neighbour, Like: like, Trust: trust, Familiarity: 50, AestheticAttraction: 0, PhysicalAttraction: 0, IntimateAffinity: 0, SexualInterest: 0,
                    Closeness: closeness, Respect: 50, Comfort: 50, Breakdown: new GameEngineTools.Characters.Engines.Relationships.DomainBreakdown(50, 50, 50, 50, 50));
            }

            var friend = Edge(80, 70, 50);

            Assert.IsTrue(ToolLendingPolicy.Probability(friend, 0.5, ownerNeedsItNow: false) > 0.8);
            Assert.IsTrue(ToolLendingPolicy.Probability(friend, 0.5, ownerNeedsItNow: true) < 0.1, "The owner needs it now.");
            Assert.AreEqual(0.0, ToolLendingPolicy.Probability(null, 0.5, ownerNeedsItNow: false), "A stranger has no standing.");
            Assert.IsTrue(ToolLendingPolicy.Probability(Edge(40, 40, 30), 0.5, false) < 0.15, "Lukewarm is not enough.");
            Assert.IsTrue(ToolLendingPolicy.Probability(Edge(50, 50, 45), 1.0, false) > ToolLendingPolicy.Probability(Edge(50, 50, 45), 0.0, false), "The same edge: an agreeable owner lends more readily.");
            Assert.IsTrue(ToolLendingPolicy.Probability(Edge(60, 60, 60), 0.5, false) > ToolLendingPolicy.Probability(Edge(50, 50, 50), 0.5, false), "More trust, like and closeness: more likely.");
            Assert.AreEqual(0.0, ToolLendingPolicy.Probability(friend with { IsContemptuouslyDestroyed = true }, 0.5, false));
        }

        [TestMethod]
        public void ARefusal_IsSortedByItsCause()
        {
            RelationshipEdge Edge(double trust, double like, double closeness)
            {
                return new(Owner, Neighbour, Like: like, Trust: trust, Familiarity: 50, AestheticAttraction: 0, PhysicalAttraction: 0, IntimateAffinity: 0, SexualInterest: 0,
                    Closeness: closeness, Respect: 50, Comfort: 50, Breakdown: new GameEngineTools.Characters.Engines.Relationships.DomainBreakdown(50, 50, 50, 50, 50));
            }

            Assert.AreEqual(ToolRefusalReason.NeededNow, ToolLendingPolicy.ClassifyRefusal(Edge(80, 70, 50), 0.5, true));
            Assert.AreEqual(ToolRefusalReason.NoStanding, ToolLendingPolicy.ClassifyRefusal(null, 0.5, false));
            Assert.AreEqual(ToolRefusalReason.NoStanding, ToolLendingPolicy.ClassifyRefusal(Edge(80, 70, 50) with { IsContemptuouslyDestroyed = true }, 0.5, false));
            Assert.AreEqual(ToolRefusalReason.BelowThreshold, ToolLendingPolicy.ClassifyRefusal(Edge(40, 40, 30), 0.5, false));
            Assert.AreEqual(ToolRefusalReason.Unlucky, ToolLendingPolicy.ClassifyRefusal(Edge(80, 70, 50), 0.5, false));
        }

        [TestMethod]
        public void TheOwnerNeedsATool_OnlyOnAShiftAndWhereTheToolLies()
        {
            Assert.IsTrue(ToolLendingPolicy.OwnerNeedsItNow(true, "farm_a", "farm_a"));
            Assert.IsFalse(ToolLendingPolicy.OwnerNeedsItNow(true, "farm_a", "farm_b"), "A tool at another place is not missed.");
            Assert.IsFalse(ToolLendingPolicy.OwnerNeedsItNow(false, "farm_a", "farm_a"), "Off shift nothing is needed.");
            Assert.IsFalse(ToolLendingPolicy.OwnerNeedsItNow(true, null, "farm_a"), "An owner with no known place does not guard it.");
        }
    }
}
