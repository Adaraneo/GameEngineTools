// HabitLearningTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Behavior.Modifiers;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
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

        #endregion Tests

        #region Helpers

        private static DefaultBehaviorEngine BuildEngine()
            => new(
                Options.Create(new BehaviorConfig()),
                Options.Create(new SleepConfig()),
                LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning)));

        #endregion Helpers
    }
}
