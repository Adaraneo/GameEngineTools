// HabitLearningTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using static GameEngineTools.Characters.Engines.ActionNames;

    [TestClass]
    public class HabitLearningTests : TestBase
    {
        #region Tests

        [TestMethod]
        public void Handle_RepeatedCompetenceAction_BuildsAdaptiveHabit()
        {
            var engine = BuildEngine();
            var context = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(9).Ticks),
                state: new BehaviorState(10, 5, 5, 20, 80, 20, null),
                surfaceKind: SurfaceKind.Work,
                competence: 1.0);

            engine.RestoreState(context.State);

            for (var i = 0; i < 5; i++)
            {
                engine.Handle(
                    new ActionCommitted(context.Now + WTimeSpan.FromMinutes(i), context.HumanContext.Id, Work, WTimeSpan.FromHours(1)),
                    context.HumanContext,
                    new EventCollector());
            }

            var habit = engine.State.HabitTraces!.Values.Single(trace => trace.ActionName == Work);
            Assert.AreEqual(HabitTendency.Adaptive, habit.Tendency);
            Assert.IsTrue(habit.Strength > 0.20);
            Assert.IsTrue(habit.AdaptiveReinforcement > habit.CopingReinforcement);
        }

        [TestMethod]
        public void Handle_RepeatedStressIdle_BuildsMaladaptiveCopingHabit()
        {
            var engine = BuildEngine();
            var context = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(21).Ticks),
                state: new BehaviorState(10, 5, 5, 15, 30, 10, null),
                surfaceKind: SurfaceKind.Private,
                stress: 90,
                valence: -0.5);

            engine.RestoreState(context.State);

            for (var i = 0; i < 5; i++)
            {
                engine.Handle(
                    new ActionCommitted(context.Now + WTimeSpan.FromMinutes(i), context.HumanContext.Id, Idle, WTimeSpan.FromMinutes(30)),
                    context.HumanContext,
                    new EventCollector());
            }

            var habit = engine.State.HabitTraces!.Values.Single(trace => trace.ActionName == Idle);
            Assert.AreEqual(HabitCueKind.StressRelief, habit.CueKind);
            Assert.AreEqual(HabitTendency.MaladaptiveCoping, habit.Tendency);
            Assert.IsTrue(habit.CopingReinforcement > habit.AdaptiveReinforcement);
        }

        [TestMethod]
        public void Modify_LearnedHabit_BiasesMatchingCueAction()
        {
            var traces = new Dictionary<string, BehaviorHabitTrace>
            {
                ["work-morning"] = new BehaviorHabitTrace(
                    Work,
                    SurfaceKind.Work,
                    HabitTimeBand.Morning,
                    HabitCueKind.CompetenceNeed,
                    Strength: 0.80,
                    AdaptiveReinforcement: 0.60,
                    CopingReinforcement: 0.05,
                    RepetitionCount: 6,
                    LastUpdatedAt: new WDateTime(0),
                    HabitTendency.Adaptive)
            };
            var state = new BehaviorState(10, 5, 5, 20, 80, 20, null, HabitTraces: traces);
            var context = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(8).Ticks),
                state: state,
                surfaceKind: SurfaceKind.Work,
                competence: 1.0);
            var candidates = new List<BehaviorCandidate>
            {
                new(Work, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence),
                new(Create, 10, WTimeSpan.FromHours(1), BehaviorDomain.Competence)
            };

            new LearnedHabitEngine().Modify(context, candidates);

            Assert.IsTrue(candidates.Single(candidate => candidate.Name == Work).Utility > candidates.Single(candidate => candidate.Name == Create).Utility);
        }

        [TestMethod]
        public void Decay_SameElapsedTime_IsEquivalentAcrossTickGranularity()
        {
            var config = new BehaviorConfig(HabitDecayPerDay: 0.10);
            var traces = new Dictionary<string, BehaviorHabitTrace>
            {
                ["work"] = Trace(Work, SurfaceKind.Work, HabitTimeBand.Morning, HabitCueKind.CompetenceNeed, strength: 0.80, lastUpdatedAt: new WDateTime(0), repetitionCount: 6)
            };

            var oneStep = BehaviorHabitLearning.Decay(traces, WTimeSpan.FromDays(2), config)!;
            IReadOnlyDictionary<string, BehaviorHabitTrace>? manySteps = traces;
            for (var i = 0; i < 8; i++)
            {
                manySteps = BehaviorHabitLearning.Decay(manySteps, WTimeSpan.FromDays(0.25), config);
            }

            Assert.AreEqual(oneStep["work"].Strength, manySteps!["work"].Strength, 0.0001);
        }

        [TestMethod]
        public void Decay_WeakensAdaptiveAndCopingReinforcementMoreMildlyThanStrength()
        {
            var config = new BehaviorConfig(HabitDecayPerDay: 0.10);
            var traces = new Dictionary<string, BehaviorHabitTrace>
            {
                ["work"] = new BehaviorHabitTrace(
                    Work,
                    SurfaceKind.Work,
                    HabitTimeBand.Morning,
                    HabitCueKind.CompetenceNeed,
                    Strength: 0.80,
                    AdaptiveReinforcement: 0.60,
                    CopingReinforcement: 0.40,
                    RepetitionCount: 6,
                    LastUpdatedAt: new WDateTime(0),
                    HabitTendency.Adaptive)
            };

            var decayed = BehaviorHabitLearning.Decay(traces, WTimeSpan.FromDays(2), config)!["work"];

            Assert.IsTrue(decayed.Strength < 0.80);
            Assert.IsTrue(decayed.AdaptiveReinforcement < 0.60);
            Assert.IsTrue(decayed.CopingReinforcement < 0.40);
            Assert.IsTrue(decayed.AdaptiveReinforcement / 0.60 > decayed.Strength / 0.80);
            Assert.IsTrue(decayed.CopingReinforcement / 0.40 > decayed.Strength / 0.80);
        }

        [TestMethod]
        public void ComputeCandidateBias_UsesBoundedWeightedAggregationAcrossTopHabitTraces()
        {
            var traces = new Dictionary<string, BehaviorHabitTrace>(StringComparer.Ordinal)
            {
                ["primary"] = Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 0.35, lastUpdatedAt: new WDateTime(0), repetitionCount: 4),
                ["secondary"] = Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 0.30, lastUpdatedAt: new WDateTime(0), repetitionCount: 3),
                ["tertiary"] = Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 0.20, lastUpdatedAt: new WDateTime(0), repetitionCount: 2)
            };
            var state = new BehaviorState(90, 5, 5, 20, 20, 20, null, HabitTraces: traces);
            var context = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(8).Ticks),
                state: state,
                surfaceKind: SurfaceKind.Private,
                stress: 0,
                hunger: 5,
                thirst: 5,
                energy: 40);
            var candidate = new BehaviorCandidate(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological);

            var bias = BehaviorHabitLearning.ComputeCandidateBias(context, candidate);
            var singleTraceContext = context with
            {
                State = context.State with
                {
                    HabitTraces = new Dictionary<string, BehaviorHabitTrace>(StringComparer.Ordinal)
                    {
                        ["primary"] = traces["primary"]
                    }
                }
            };
            var singleTraceBias = BehaviorHabitLearning.ComputeCandidateBias(singleTraceContext, candidate);

            Assert.IsTrue(bias > singleTraceBias, "Multiple applicable traces should aggregate above a pure max trace.");
            Assert.IsTrue(bias <= 1.0);
        }

        [TestMethod]
        public void LearnFromCommitment_CueCongruentBeneficialAction_ReinforcesMoreThanWeakFitForcedAction()
        {
            var self = new HumanId(Guid.NewGuid());
            var beneficialState = new BehaviorState(10, 5, 5, 20, 92, 20, null);
            var weakState = new BehaviorState(10, 5, 5, 20, 5, 20, null);
            var beneficialContext = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(9).Ticks),
                state: beneficialState,
                surfaceKind: SurfaceKind.Work,
                competence: 1.0,
                selfId: self).HumanContext;
            var weakContext = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(9).Ticks),
                state: weakState,
                surfaceKind: SurfaceKind.Work,
                competence: 0.0,
                selfId: self).HumanContext;

            var beneficial = BehaviorHabitLearning.LearnFromCommitment(
                beneficialState,
                new ActionCommitted(new WDateTime(WTimeSpan.FromHours(9).Ticks), self, Work, WTimeSpan.FromHours(1), IntendedActionName: Work),
                beneficialContext,
                new BehaviorConfig());
            var weak = BehaviorHabitLearning.LearnFromCommitment(
                weakState,
                new ActionCommitted(new WDateTime(WTimeSpan.FromHours(9).Ticks), self, Work, WTimeSpan.FromHours(1), IntendedActionName: Idle, ConflictReason: "forced_by_context"),
                weakContext,
                new BehaviorConfig());

            Assert.IsTrue(beneficial.HabitTraces!.Values.Single().Strength > weak.HabitTraces!.Values.Single().Strength);
            Assert.IsTrue(beneficial.HabitTraces!.Values.Single().AdaptiveReinforcement > weak.HabitTraces!.Values.Single().AdaptiveReinforcement);
        }

        [TestMethod]
        public void LearnFromCommitment_MoveToPrivateUnderStress_UsesStressReliefCue()
        {
            var self = new HumanId(Guid.NewGuid());
            var state = new BehaviorState(20, 5, 5, 35, 15, 15, null);
            var context = BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(22).Ticks),
                state: state,
                surfaceKind: SurfaceKind.Private,
                stress: 90,
                valence: -0.45,
                selfId: self).HumanContext;

            var learned = BehaviorHabitLearning.LearnFromCommitment(
                state,
                new ActionCommitted(new WDateTime(WTimeSpan.FromHours(22).Ticks), self, MoveToPrivate, WTimeSpan.FromMinutes(30), IntendedActionName: MoveToPrivate),
                context,
                new BehaviorConfig());

            var trace = learned.HabitTraces!.Values.Single();
            Assert.AreEqual(HabitCueKind.StressRelief, trace.CueKind);
        }

        [TestMethod]
        public void LearnFromCommitment_PrunesWeakStaleTraceBeforeStrongRecentTrace()
        {
            var self = new HumanId(Guid.NewGuid());
            var now = new WDateTime(WTimeSpan.FromDays(30).Ticks);
            var context = BehaviorComponentTestFactory.Context(
                now: now,
                state: new BehaviorState(10, 5, 5, 20, 80, 20, null),
                surfaceKind: SurfaceKind.Work,
                competence: 1.0,
                selfId: self);
            var traces = Enumerable.Range(0, 10).ToDictionary(
                i => $"trace-{i}",
                i => Trace(
                    actionName: i == 0 ? Create : Work,
                    surface: SurfaceKind.Work,
                    timeBand: HabitTimeBand.Morning,
                    cue: HabitCueKind.CompetenceNeed,
                    strength: i == 0 ? 0.02 : i == 1 ? 0.90 : 0.30 + i * 0.01,
                    lastUpdatedAt: i == 0 ? new WDateTime(0) : now,
                    repetitionCount: i == 1 ? 8 : 1),
                StringComparer.Ordinal);
            var state = context.State with { HabitTraces = traces };

            var learned = BehaviorHabitLearning.LearnFromCommitment(
                state,
                new ActionCommitted(now, self, Work, WTimeSpan.FromHours(1), IntendedActionName: Work),
                context.HumanContext,
                new BehaviorConfig(MaxHabitTraces: 8));

            Assert.IsFalse(learned.HabitTraces!.ContainsKey("trace-0"));
            Assert.IsTrue(learned.HabitTraces!.ContainsKey("trace-1"));
            Assert.IsTrue(learned.HabitTraces!.Count <= 8);
        }

        [TestMethod]
        public void ComputeCandidateBias_NoExternalModulator_MatchesBaselineApplicability()
        {
            var trace = Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 0.70, lastUpdatedAt: new WDateTime(0), repetitionCount: 4);
            var context = ContextWithTrace(trace, nowHour: 8, surfaceKind: SurfaceKind.Private, needRest: 90);
            var candidate = new BehaviorCandidate(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological);

            var baseline = BehaviorHabitLearning.ComputeCandidateBias(context, candidate);
            var modulated = BehaviorHabitLearning.ComputeCandidateBias(context, candidate, NoOpHabitApplicabilityModulator.Instance);

            Assert.AreEqual(baseline, modulated, 0.0001);
        }

        [TestMethod]
        public void Modify_LearnedHabit_BiasIsBoundedByConfig()
        {
            var context = ContextWithTrace(
                Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 1.0, lastUpdatedAt: new WDateTime(0), repetitionCount: 20),
                nowHour: 8,
                surfaceKind: SurfaceKind.Private,
                needRest: 90) with
            {
                Config = new BehaviorConfig(HabitMaxUtilityMultiplier: 0.10, HabitMaxFlatBias: 2.0)
            };
            var candidates = new List<BehaviorCandidate> { new(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological) };

            new LearnedHabitEngine().Modify(context, candidates);

            Assert.IsTrue(candidates[0].Utility <= 13.0001);
        }

        [TestMethod]
        public void ComputeCandidateBias_RelatedContextGeneralizesButUnrelatedContextStaysWeak()
        {
            var trace = Trace(SelfCare, SurfaceKind.Private, HabitTimeBand.Morning, HabitCueKind.BodyNeed, strength: 0.80, lastUpdatedAt: new WDateTime(0), repetitionCount: 5);
            var exact = ContextWithTrace(trace, nowHour: 8, surfaceKind: SurfaceKind.Private, needRest: 90);
            var related = ContextWithTrace(trace, nowHour: 11, surfaceKind: SurfaceKind.Rest, needRest: 90);
            var unrelated = ContextWithTrace(trace, nowHour: 14, surfaceKind: SurfaceKind.Work, needRest: 90);
            var candidate = new BehaviorCandidate(SelfCare, 10, WTimeSpan.FromHours(1), BehaviorDomain.Physiological);

            var exactBias = BehaviorHabitLearning.ComputeCandidateBias(exact, candidate);
            var relatedBias = BehaviorHabitLearning.ComputeCandidateBias(related, candidate);
            var unrelatedBias = BehaviorHabitLearning.ComputeCandidateBias(unrelated, candidate);

            Assert.IsTrue(relatedBias > 0.0);
            Assert.IsTrue(relatedBias < exactBias);
            Assert.IsTrue(unrelatedBias < relatedBias);
        }

        #endregion Tests

        #region Helpers

        private static DefaultBehaviorEngine BuildEngine()
            => new(
                Options.Create(new BehaviorConfig()),
                Options.Create(new SleepConfig()),
                LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning)));

        private static BehaviorContext ContextWithTrace(
            BehaviorHabitTrace trace,
            int nowHour,
            SurfaceKind surfaceKind,
            double needRest)
        {
            var state = new BehaviorState(
                needRest,
                5,
                5,
                20,
                20,
                20,
                null,
                HabitTraces: new Dictionary<string, BehaviorHabitTrace>(StringComparer.Ordinal)
                {
                    ["trace"] = trace
                });

            return BehaviorComponentTestFactory.Context(
                now: new WDateTime(WTimeSpan.FromHours(nowHour).Ticks),
                state: state,
                surfaceKind: surfaceKind,
                stress: 0,
                hunger: 5,
                thirst: 5,
                energy: 95);
        }

        private static BehaviorHabitTrace Trace(
            string actionName,
            SurfaceKind surface,
            HabitTimeBand timeBand,
            HabitCueKind cue,
            double strength,
            WDateTime lastUpdatedAt,
            int repetitionCount)
            => new(
                actionName,
                surface,
                timeBand,
                cue,
                Strength: strength,
                AdaptiveReinforcement: cue == HabitCueKind.CompetenceNeed || cue == HabitCueKind.BodyNeed ? strength * 0.60 : 0.05,
                CopingReinforcement: cue == HabitCueKind.StressRelief ? strength * 0.60 : 0.05,
                RepetitionCount: repetitionCount,
                LastUpdatedAt: lastUpdatedAt,
                Tendency: cue == HabitCueKind.StressRelief ? HabitTendency.MaladaptiveCoping : HabitTendency.Adaptive);

        #endregion Helpers
    }
}
