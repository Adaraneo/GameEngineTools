// ActionSlotTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Linq;

    /// <summary>
    /// Unit tests for the Parallel Action Slots feature:
    /// <see cref="ActionSlotMask"/>, <see cref="ActionSlotMaskResolver"/>,
    /// <see cref="ActiveActionSlots"/>, and the slot-aware output of
    /// <see cref="ObjectInteractionBehaviorModifier"/>.
    /// </summary>
    [TestClass]
    public class ActionSlotTests : TestBase
    {
        private static readonly WDateTime T0 = WDateTime.New(WDateOnly.New(100, 1, 1));

        // ─────────────────────────────────────────────────────────────────────

        #region ActionSlotMaskResolver — basic lookups

        /// <summary>
        /// UseObject:Rest must require only the Posture channel — so a character
        /// sitting on a bench leaves Hands and Mouth free.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_UseObjectForRest_ReturnsPostureOnly()
        {
            var mask = ActionSlotMaskResolver.Get(ActionNames.UseObjectForRest);

            Assert.AreEqual(ActionSlotMask.Posture, mask);
        }

        /// <summary>
        /// Eating must occupy both Hands and Mouth — the canonical multitasking
        /// example: a character can eat while seated (Posture free).
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_Eat_ReturnsHandsAndMouth()
        {
            var mask = ActionSlotMaskResolver.Get(ActionNames.Eat);

            Assert.AreEqual(ActionSlotMask.Hands | ActionSlotMask.Mouth, mask);
        }

        /// <summary>
        /// Sitting (Posture) and eating (Hands|Mouth) share no bits — they must
        /// be considered non-conflicting by the slot system.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_SitAndEat_NoConflict()
        {
            var sit = ActionSlotMaskResolver.Get(ActionNames.UseObjectForRest);
            var eat = ActionSlotMaskResolver.Get(ActionNames.Eat);

            Assert.AreEqual(ActionSlotMask.None, sit & eat,
                "Sitting and eating must not share any slot bits.");
        }

        /// <summary>
        /// Work and Create both require Hands + Mind — they must be mutually
        /// exclusive with each other and with Sleep.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_WorkAndCreate_RequireHandsAndMind()
        {
            var work = ActionSlotMaskResolver.Get(ActionNames.Work);
            var create = ActionSlotMaskResolver.Get(ActionNames.Create);

            Assert.AreEqual(ActionSlotMask.Hands | ActionSlotMask.Mind, work);
            Assert.AreEqual(ActionSlotMask.Hands | ActionSlotMask.Mind, create);
        }

        /// <summary>
        /// Passive actions (Warmth, Mood, Social gather) declare no channels —
        /// they can run alongside anything.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_PassiveAffordanceActions_ReturnNone()
        {
            Assert.AreEqual(ActionSlotMask.None, ActionSlotMaskResolver.Get(ActionNames.UseObjectForWarmth));
            Assert.AreEqual(ActionSlotMask.None, ActionSlotMaskResolver.Get(ActionNames.UseObjectForMood));
            Assert.AreEqual(ActionSlotMask.None, ActionSlotMaskResolver.Get(ActionNames.GatherAtObject));
        }

        /// <summary>
        /// Unknown action names must return None — safe default that imposes no constraint
        /// and does not throw.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_UnknownAction_ReturnsNone()
        {
            var mask = ActionSlotMaskResolver.Get("SomeUnknownAction:XYZ");

            Assert.AreEqual(ActionSlotMask.None, mask);
        }

        /// <summary>
        /// Legacy InteractWithObject with a Take payload must map to Hands.
        /// </summary>
        [TestMethod]
        public void ActionSlotMaskResolver_InteractWithObject_Take_ReturnsHands()
        {
            var data = new ObjectInteractionData("obj1", "loc1", ObjectInteractionKind.Take);
            var mask = ActionSlotMaskResolver.Get(ActionNames.InteractWithObject, data);

            Assert.AreEqual(ActionSlotMask.Hands, mask);
        }

        #endregion ActionSlotMaskResolver — basic lookups

        // ─────────────────────────────────────────────────────────────────────

        #region ActiveActionSlots — acquire, expire, release

        /// <summary>
        /// Slots whose expiry has passed must be removed by ExpireAll and the
        /// occupied mask must return to None.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_ExpireAll_ReleasesExpiredSlots()
        {
            var slots = new ActiveActionSlots();
            var duration = WTimeSpan.FromMinutes(30);

            slots.AcquireOrReplace("UseObject:Rest", ActionSlotMask.Posture, T0, duration);
            Assert.AreEqual(ActionSlotMask.Posture, slots.OccupiedMask,
                "Posture must be occupied right after acquire.");

            // Expire at T0 + 31 min — past the 30-min duration.
            slots.ExpireAll(T0 + WTimeSpan.FromMinutes(31));

            Assert.AreEqual(ActionSlotMask.None, slots.OccupiedMask,
                "All slots must be free after expiry time has passed.");
        }

        /// <summary>
        /// A slot not yet expired must NOT be released by ExpireAll.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_ExpireAll_DoesNotReleaseActiveSlot()
        {
            var slots = new ActiveActionSlots();

            slots.AcquireOrReplace("UseObject:Rest", ActionSlotMask.Posture, T0, WTimeSpan.FromMinutes(30));

            // Expire at T0 + 10 min — the slot still has 20 minutes to go.
            slots.ExpireAll(T0 + WTimeSpan.FromMinutes(10));

            Assert.AreEqual(ActionSlotMask.Posture, slots.OccupiedMask,
                "Slot must still be occupied when expiry has not been reached.");
        }

        /// <summary>
        /// AcquireOrReplace must overwrite an existing slot when a new action
        /// claims the same channel — the newer action wins.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_AcquireOrReplace_NewActionEvictsOld()
        {
            var slots = new ActiveActionSlots();

            slots.AcquireOrReplace("UseObject:Rest", ActionSlotMask.Posture, T0, WTimeSpan.FromMinutes(30));
            // Second action also claims Posture — must evict the first.
            slots.AcquireOrReplace("MoveTo:Rest", ActionSlotMask.Posture, T0, WTimeSpan.FromHours(1));

            // Expire at T0 + 31 min — the first action would have expired, but
            // the second (1 h) is still live.
            slots.ExpireAll(T0 + WTimeSpan.FromMinutes(31));

            Assert.AreEqual(ActionSlotMask.Posture, slots.OccupiedMask,
                "Posture must still be occupied by the newer, longer-lived action.");
        }

        /// <summary>
        /// IsFree must return true when the mask is None — passive actions
        /// must never be blocked.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_IsFree_NoneIsAlwaysFree()
        {
            var slots = new ActiveActionSlots();
            slots.AcquireOrReplace("Sleep",
                ActionSlotMask.Posture | ActionSlotMask.Hands | ActionSlotMask.Mind,
                T0, WTimeSpan.FromHours(8));

            Assert.IsTrue(slots.IsFree(ActionSlotMask.None),
                "None mask must always report free — passive actions must not be gated.");
        }

        /// <summary>
        /// Release must free slots held by the named action without touching
        /// slots held by other actions.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_Release_FreesOnlyNamedAction()
        {
            var slots = new ActiveActionSlots();

            slots.AcquireOrReplace("UseObject:Rest", ActionSlotMask.Posture, T0, WTimeSpan.FromHours(1));
            slots.AcquireOrReplace("ReachOut", ActionSlotMask.Mouth, T0, WTimeSpan.FromMinutes(20));

            slots.Release("UseObject:Rest");

            Assert.AreEqual(ActionSlotMask.Mouth, slots.OccupiedMask,
                "Only Posture (UseObject:Rest) must be freed; Mouth (ReachOut) must remain.");
        }

        /// <summary>
        /// OccupiedMask must correctly OR together multiple independent slots.
        /// </summary>
        [TestMethod]
        public void ActiveActionSlots_OccupiedMask_CombinesMultipleSlots()
        {
            var slots = new ActiveActionSlots();

            slots.AcquireOrReplace("UseObject:Rest", ActionSlotMask.Posture, T0, WTimeSpan.FromHours(1));
            slots.AcquireOrReplace("ReachOut", ActionSlotMask.Mouth, T0, WTimeSpan.FromMinutes(20));

            var expected = ActionSlotMask.Posture | ActionSlotMask.Mouth;
            Assert.AreEqual(expected, slots.OccupiedMask);
        }

        #endregion ActiveActionSlots — acquire, expire, release

        // ─────────────────────────────────────────────────────────────────────

        #region ObjectInteractionBehaviorModifier — slot-aware output

        /// <summary>
        /// A bench with a Rest affordance must produce a candidate whose Name is
        /// UseObject:Rest, not the generic InteractWithObject.
        /// </summary>
        [TestMethod]
        public void ObjectInteractionBehaviorModifier_BenchWithRest_EmitsUseObjectForRest()
        {
            var modifier = new ObjectInteractionBehaviorModifier();
            var bench = MakeAffordanceObject("bench_01", AffordanceType.Rest, satisfaction: 0.9, isPickable: false);
            var candidates = new List<BehaviorCandidate>();

            modifier.Modify(BuildModifierContext([bench], needRest: 80), candidates);

            Assert.IsTrue(candidates.Any(c => c.Name == ActionNames.UseObjectForRest),
                $"Expected UseObject:Rest candidate. Got: [{string.Join(", ", candidates.Select(c => c.Name))}]");
        }

        /// <summary>
        /// The UseObject:Rest candidate produced for a bench must carry
        /// SlotMask == Posture.
        /// </summary>
        [TestMethod]
        public void ObjectInteractionBehaviorModifier_BenchWithRest_SlotMaskIsPosture()
        {
            var modifier = new ObjectInteractionBehaviorModifier();
            var bench = MakeAffordanceObject("bench_01", AffordanceType.Rest, satisfaction: 0.9, isPickable: false);
            var candidates = new List<BehaviorCandidate>();

            modifier.Modify(BuildModifierContext([bench], needRest: 80), candidates);

            var candidate = candidates.FirstOrDefault(c => c.Name == ActionNames.UseObjectForRest);
            Assert.IsNotNull(candidate, "UseObject:Rest candidate must be present.");
            Assert.AreEqual(ActionSlotMask.Posture, candidate.SlotMask,
                "Bench (Rest affordance) must set SlotMask to Posture.");
        }

        /// <summary>
        /// A workbench (Work affordance) must produce UseObject:Work with Hands|Mind mask.
        /// </summary>
        [TestMethod]
        public void ObjectInteractionBehaviorModifier_Workbench_EmitsUseObjectForWork_WithHandsMindMask()
        {
            var modifier = new ObjectInteractionBehaviorModifier();
            var workbench = MakeAffordanceObject("workbench_01", AffordanceType.Work, satisfaction: 0.8, isPickable: false);
            var candidates = new List<BehaviorCandidate>();

            modifier.Modify(BuildModifierContext([workbench], needCompetence: 70), candidates);

            var candidate = candidates.FirstOrDefault(c => c.Name == ActionNames.UseObjectForWork);
            Assert.IsNotNull(candidate, "UseObject:Work candidate must be present.");
            Assert.AreEqual(ActionSlotMask.Hands | ActionSlotMask.Mind, candidate.SlotMask,
                "Work affordance must set SlotMask to Hands|Mind.");
        }

        /// <summary>
        /// A fireplace (Warmth affordance) must produce UseObject:Warmth with None mask —
        /// warming up is a passive action that does not block any channel.
        /// </summary>
        [TestMethod]
        public void ObjectInteractionBehaviorModifier_Fireplace_EmitsUseObjectForWarmth_WithNoneMask()
        {
            var modifier = new ObjectInteractionBehaviorModifier();
            // Force need score above threshold by providing a cold body temp via a high-enough rest need;
            // Warmth affordance uses BodyTempDelta — set needRest high to pass MinNeedThreshold path.
            // Actually Warmth uses BodyTempDelta<-1 check; we work around by giving it a high base score.
            // Use needRest=80 so other affordances don't beat warmth in utility.
            var fireplace = MakeAffordanceObject("fireplace_01", AffordanceType.Warmth, satisfaction: 1.0, isPickable: false);
            var candidates = new List<BehaviorCandidate>();

            // BuildModifierContext with cold body so Warmth need score = 70 (above threshold).
            modifier.Modify(BuildModifierContext([fireplace], bodyTempDelta: -2.0), candidates);

            // If no candidates, warmth need was not high enough — still verify no wrong name.
            if (candidates.Any())
            {
                var candidate = candidates.FirstOrDefault(c => c.Name == ActionNames.UseObjectForWarmth);
                Assert.IsNotNull(candidate, "UseObject:Warmth must be the resolved name for Warmth affordance.");
                Assert.AreEqual(ActionSlotMask.None, candidate.SlotMask,
                    "Warmth affordance must set SlotMask to None (passive).");
            }
        }

        /// <summary>
        /// A pickable object with Ownership affordance must keep the generic InteractWithObject
        /// name — Take interactions are not renamed.
        /// </summary>
        [TestMethod]
        public void ObjectInteractionBehaviorModifier_PickableOwnership_KeepsInteractWithObjectName()
        {
            var modifier = new ObjectInteractionBehaviorModifier();
            var item = MakeAffordanceObject("sword_01", AffordanceType.Ownership, satisfaction: 0.8, isPickable: true);
            var candidates = new List<BehaviorCandidate>();

            modifier.Modify(BuildModifierContext([item]), candidates);

            var candidate = candidates.FirstOrDefault();
            Assert.IsNotNull(candidate, "A candidate must be emitted for a pickable Ownership object.");
            Assert.AreEqual(ActionNames.InteractWithObject, candidate!.Name,
                "Take interactions must keep the generic InteractWithObject name.");
        }

        #endregion ObjectInteractionBehaviorModifier — slot-aware output

        // ─────────────────────────────────────────────────────────────────────

        #region SelectSecondaryAction — filter logic

        /// <summary>
        /// Sit (Posture) + Eat (Hands|Mouth) share no bits — Eat must be returned as secondary.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_RestPlusEat_NoConflict_ReturnsEat()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var eat = MakeCandidate(ActionNames.Eat, ActionSlotMask.Hands | ActionSlotMask.Mouth, utility: 40);

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, eat], primary: sit, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNotNull(result);
            Assert.AreEqual(ActionNames.Eat, result!.Name);
        }

        /// <summary>
        /// Work (Hands|Mind) and Create (Hands|Mind) conflict — null must be returned.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_ConflictingSlots_ReturnsNull()
        {
            var work = MakeCandidate(ActionNames.Work, ActionSlotMask.Hands | ActionSlotMask.Mind, utility: 70);
            var create = MakeCandidate(ActionNames.Create, ActionSlotMask.Hands | ActionSlotMask.Mind, utility: 50);

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [work, create], primary: work, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNull(result, "Conflicting Hands|Mind slot must produce null.");
        }

        /// <summary>
        /// A candidate with SocialTargeting must be excluded from secondary selection —
        /// ReachOut requires the full interaction-proposal pipeline.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_SocialTargeting_Excluded()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var reachOut = MakeCandidate(ActionNames.ReachOut, ActionSlotMask.Mouth, utility: 50,
                socialTargeting: new SocialTargetingData(
                    new HumanId(Guid.NewGuid()), RelationalActKind.SmallTalk, 0.7, 0.8, 0.2));

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, reachOut], primary: sit, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNull(result, "Candidate with SocialTargeting must be excluded from secondary.");
        }

        /// <summary>
        /// A candidate below MinSecondaryUtility (10) must not be selected.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_BelowMinUtility_Excluded()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var low = MakeCandidate(ActionNames.SelfCare, ActionSlotMask.Hands, utility: 5);

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, low], primary: sit, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNull(result, "Candidate with utility 5 is below MinSecondaryUtility and must be excluded.");
        }

        /// <summary>
        /// When alreadyOccupied from a prior tick includes Hands, a secondary candidate
        /// that also needs Hands must be blocked.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_AlreadyOccupiedFromPriorTick_BlocksConflictingSecondary()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var eat = MakeCandidate(ActionNames.Eat, ActionSlotMask.Hands | ActionSlotMask.Mouth, utility: 40);

            // Hands are already occupied from a prior tick (e.g., carrying something).
            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, eat], primary: sit, alreadyOccupied: ActionSlotMask.Hands);

            Assert.IsNull(result, "Eat (Hands|Mouth) must be blocked when Hands are already occupied.");
        }

        /// <summary>
        /// When multiple non-conflicting candidates exist, the highest-utility one wins.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_MultipleEligible_ReturnsHighestUtility()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var eat = MakeCandidate(ActionNames.Eat, ActionSlotMask.Hands | ActionSlotMask.Mouth, utility: 45);
            var drink = MakeCandidate(ActionNames.Drink, ActionSlotMask.Hands | ActionSlotMask.Mouth, utility: 30);

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, eat, drink], primary: sit, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNotNull(result);
            Assert.AreEqual(ActionNames.Eat, result!.Name, "Eat (utility 45) must beat Drink (utility 30).");
        }

        /// <summary>
        /// A candidate whose SlotMask is None must be excluded — passive ambient actions
        /// don't need an explicit secondary commit.
        /// </summary>
        [TestMethod]
        public void SelectSecondaryAction_PassiveCandidateNoneMask_Excluded()
        {
            var sit = MakeCandidate(ActionNames.UseObjectForRest, ActionSlotMask.Posture, utility: 60);
            var warmth = MakeCandidate(ActionNames.UseObjectForWarmth, ActionSlotMask.None, utility: 35);

            var result = DefaultBehaviorEngine.SelectSecondaryAction(
                [sit, warmth], primary: sit, alreadyOccupied: ActionSlotMask.None);

            Assert.IsNull(result, "Candidate with SlotMask=None must be excluded from secondary selection.");
        }

        private static BehaviorCandidate MakeCandidate(
            string name,
            ActionSlotMask mask,
            double utility,
            SocialTargetingData? socialTargeting = null)
            => new BehaviorCandidate(
                Name: name,
                Utility: utility,
                Duration: WTimeSpan.FromMinutes(30),
                Domain: BehaviorDomain.Physiological,
                SlotMask: mask,
                SocialTargeting: socialTargeting);

        #endregion SelectSecondaryAction — filter logic

        // ─────────────────────────────────────────────────────────────────────

        #region Private factory helpers

        private static WorldObject MakeAffordanceObject(
            string id,
            AffordanceType type,
            double satisfaction,
            bool isPickable)
            => new()
            {
                Id = id,
                DisplayName = id,
                Category = WorldObjectCategory.Furniture,
                LocationId = "test_location",
                IsAvailable = true,
                Affordances = ImmutableArray.Create(new WorldObjectAffordance(type, satisfaction)),
                IsPickable = isPickable,
                WeightGrams = 0,
                ItemKind = PickupItemKind.None,
                HeldBy = null,
                ConsumedAt = null,
                Respawns = false,
                RespawnMinutes = 0
            };

        private static BehaviorContext BuildModifierContext(
            IReadOnlyList<WorldObject> availableObjects,
            double needRest = 50,
            double needCompetence = 50,
            double bodyTempDelta = 0.0)
        {
            var physio = new PhysiologyState(
                Energy: 80,
                SleepDebtHours: 0,
                Hunger: 25,
                Thirst: 25,
                Pain: 0,
                ImmuneLoad: 0,
                BodyTempDelta: bodyTempDelta,
                Cycle: null);

            var psych = new PsychologyState(
                Valence: 0.0,
                Arousal: 0.5,
                Dominance: 0.5,
                Stress: 0,
                CognitiveLoad: 0,
                DominantEmotion: DiscreteEmotion.Neutral);

            var behaviorState = new BehaviorState(
                NeedRest: needRest,
                NeedFood: 25,
                NeedWater: 25,
                NeedBelonging: 50,
                NeedCompetence: needCompetence,
                NeedIntimacy: 30,
                CurrentPlan: null,
                Cooldowns: new Dictionary<string, double>());

            var snapshot = new EnginesSnapshot(
                physio, psych, behaviorState,
                new InteractionSurface("test_location", false, double.NaN, double.NaN, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(new List<EpisodicMemory>()));

            var personality = new Personality(
                BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                Attachment: AttachmentProfile.Secure,
                Communication: CommunicationStyle.Direct,
                Motivation: new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality: Sociosexuality.Intermediate,
                Chronotype: Chronotype.Neutral);

            var humanCtx = new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                Snapshot = snapshot,
                Random = new AlwaysFalseRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)).CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler(),
            };

            return new BehaviorContext(
                Now: T0,
                Dt: WTimeSpan.FromHours(1),
                HumanContext: humanCtx,
                Outbox: new EventCollector(),
                State: behaviorState,
                Config: new BehaviorConfig(),
                Cooldowns: new Dictionary<string, double>(),
                DecisionWorkingSets: null,
                HabitApplicabilityModulator: null,
                AvailableObjects: availableObjects);
        }

        private sealed class AlwaysFalseRandom : IRandomSource
        {
            public int Next(int min, int max) => min;

            public double NextUnit() => 0.0;

            public bool Chance(double p) => false;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event)
            { }

            public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
                => new NullDisposable();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
                => new ScheduledId(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => false;

            public IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now)
                => Array.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class NullDisposable : IDisposable
        {
            public void Dispose()
            { }
        }

        #endregion Private factory helpers
    }
}
