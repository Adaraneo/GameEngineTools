// SleepRhythmTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Two-process sleep regulation (Process S + circadian Process C) must give a character a nightly sleep on any day length.
    /// </summary>
    [TestClass]
    public class SleepRhythmTests : TestBase
    {
        private const int Days = 8;

        private sealed record Session(double StartHour, double Hours);

        [TestMethod]
        public void SleepPropensity_IsPositiveAtNightAndNegativeInTheAfternoon_ForAnyDayLength()
        {
            var cfg = new PhysiologyConfig();
            foreach (var hoursPerDay in new[] { 12, 24, 26, 40 })
            {
                var evening = SleepRegulationCalculator.DebtFreeLevel(cfg, hoursPerDay); // S after a normal waking day
                var night = SleepRegulationCalculator.ComputeSleepPropensity(evening, hoursPerDay * 0.0, hoursPerDay, 0, cfg);
                var afternoon = SleepRegulationCalculator.ComputeSleepPropensity(evening, hoursPerDay * 16.0 / 24.0, hoursPerDay, 0, cfg);

                Assert.IsTrue(night > 0.05, $"{hoursPerDay} h day: propensity at midnight with a normal evening S was {night:F2}.");
                Assert.IsTrue(afternoon < 0.0, $"{hoursPerDay} h day: propensity at 16:00 was {afternoon:F2}; the circadian gate should block sleep.");
            }
        }

        [TestMethod]
        public void NeedRest_RisesWithPositivePropensity_AndIgnoresNegativeOnes()
        {
            double Need(double? propensity)
            {
                var ctx = BuildContext(propensity);
                return BehaviorMath.ComputeNeedState(ctx, new Dictionary<string, double>(), ctx.Snapshot.Behavior).NeedRest;
            }

            var unknown = Need(null);
            var tired = Need(0.2);
            var gated = Need(-0.3);

            Assert.AreEqual(unknown, gated, 1e-9, "A negative propensity (circadian gate) must not lower NeedRest below the physical need.");
            Assert.AreEqual(BehaviorMath.SleepPropensityNeedWeight * 0.2, tired - unknown, 1e-6);
        }

        private static IHumanContext BuildContext(double? propensity)
        {
            var personality = new Personality(
                new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                AttachmentProfile.Secure,
                CommunicationStyle.Direct,
                new MotivationWeights(0.5, 0.5, 0.3, 0.4, 0.5, 0.5, 0.5, 0.6, 0.4),
                Sociosexuality.Intermediate,
                Chronotype.Neutral);

            var snapshot = new EnginesSnapshot(
                new PhysiologyState(80, 0, 10, 10, 0, 0, 0, null) with { SleepPropensity = propensity },
                new PsychologyState(0.1, 0.4, 0.5, 10, 10, DiscreteEmotion.Neutral),
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(null, false, 0.2, 0.2, SurfaceKind.Unknown),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new GameEngineTools.Characters.Engines.Memory.MemoryIndex(new List<GameEngineTools.Characters.Engines.Memory.EpisodicMemory>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = personality,
                PsychologyProfile = PsychologicalProfile.FromPersonality(personality),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance,
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        [TestMethod]
        public void Character_SleepsOncePerNight_OnAnyDayLength()
        {
            foreach (var seed in new[] { 7, 21, 99 })
            foreach (var hoursPerDay in new[] { 24, 26, 40 })
            {
                var all = SimulateSessions(hoursPerDay, seed, out var alive);
                // A sleep cut short by an ambush is not a night of its own; the character goes back to bed afterwards.
                var sessions = all.Where(x => x.Hours >= 4.0).ToList();
                var detail = string.Join("; ", all.Select(x => $"start {x.StartHour:F1}h for {x.Hours:F1}h"));

                Assert.IsTrue(alive, $"seed {seed}, {hoursPerDay} h day: the character must survive {Days} days. {detail}");
                Assert.IsTrue(sessions.Count >= Days - 2, $"seed {seed}, {hoursPerDay} h day: expected about one sleep per day over {Days} days, got {sessions.Count}. {detail}");

                for (var i = 1; i < sessions.Count; i++)
                {
                    var gapDays = (sessions[i].StartHour - sessions[i - 1].StartHour) / hoursPerDay;
                    Assert.IsTrue(gapDays is > 0.6 and < 1.5, $"seed {seed}, {hoursPerDay} h day: gap between sleeps was {gapDays:F2} days. {detail}");
                }

                // After the first (start-up) sleep, bedtime must fall in the circadian evening/night: 18:00-05:00 on a 24-hour clock.
                foreach (var session in sessions.Skip(1))
                {
                    var clock24 = session.StartHour % hoursPerDay * 24.0 / hoursPerDay;
                    Assert.IsTrue(clock24 >= 18.0 || clock24 <= 5.0, $"seed {seed}, {hoursPerDay} h day: went to sleep at {clock24:F1} on a 24-hour clock. {detail}");
                }
            }
        }

        private List<Session> SimulateSessions(int hoursPerDay, int seed, out bool alive)
        {
            UseDayLength(hoursPerDay);
            var generator = ServiceProvider.GetRequiredService<IHumanBlueprintGenerator>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var now = WDateTime.New(116, 1, 1, 0);
            var birth = now.Date.AddYears(-30);
            var human = factory.Create(generator.Generate(new HumanBlueprintRequest(
                Sex: SexBiology.Male, MinBirthDate: birth, MaxBirthDate: birth, Seed: seed)));

            var step = WTimeSpan.FromMinutes(15);
            var sessions = new List<Session>();
            var wasAsleep = false;
            var startedAt = 0.0;

            for (var i = 0; i < Days * hoursPerDay * 4; i++)
            {
                human.Tick(now, step);

                // The scene confirms sleep prompts automatically for NPCs.
                var prompt = human.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
                if (prompt != null)
                    human.ReceiveEvent(new SleepConfirmed(now, prompt.Human, default));

                var asleep = human.Snapshot.Behavior.CurrentPlan?.Name == ActionNames.Sleep;
                if (!asleep)
                {
                    // No world here, so keep the character fed; restoring while asleep would drop the session.
                    human.RestoreSnapshot(human.Snapshot with { Physiology = human.Snapshot.Physiology with { Hunger = 15, Thirst = 15 } });
                }

                var hours = i * 0.25;
                if (asleep && !wasAsleep) startedAt = hours;
                if (!asleep && wasAsleep) sessions.Add(new Session(startedAt, hours - startedAt));
                wasAsleep = asleep;
                now += step;
            }

            alive = human.Snapshot.Physiology.Status == StatusType.Alive;
            return sessions;
        }
    }
}
