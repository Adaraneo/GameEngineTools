// SleepRhythmTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
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
    /// Two-process sleep regulation (Process S + circadian Process C, Borbély 1982; Daan, Beersma &amp; Borbély 1984) must give a
    /// character a sensible sleep rhythm on any day length.
    /// </summary>
    [TestClass]
    public class SleepRhythmTests : TestBase
    {
        private const int Days = 8;

        private sealed record Session(double StartHour, double Hours);

        [TestMethod]
        public void SleepPropensity_IsAboutZeroAtHabitualBedtime_AndFollowsTheCircadianThreshold_ForAnyDayLength()
        {
            var cfg = new PhysiologyConfig();
            foreach (var hoursPerDay in new[] { 20, 24, 26, 40 })
            {
                var evening = SleepRegulationCalculator.DebtFreeLevel(cfg, hoursPerDay); // S after a normal waking day
                double At(double clock24) => SleepRegulationCalculator.ComputeSleepPropensity(evening, hoursPerDay * clock24 / 24.0, hoursPerDay, 0, cfg);

                // The model's bedtime is where S meets H(t): propensity is about zero there, not far above or below.
                Assert.AreEqual(0.0, At(23.5), 0.08, $"{hoursPerDay} h day: propensity at habitual bedtime was {At(23.5):F2}.");
                // The swing between the afternoon peak and the night trough of the threshold is twice the amplitude (2 x 0.12).
                Assert.AreEqual(0.24, At(4.0) - At(16.0), 0.01, $"{hoursPerDay} h day.");
                Assert.IsTrue(At(16.0) < -0.05, $"{hoursPerDay} h day: at 16:00 the circadian gate should block sleep, got {At(16.0):F2}.");
            }
        }

        [TestMethod]
        public void NeedRest_RisesWithPropensity_FromTheOnsetLead_AndIgnoresStrongNegativeOnes()
        {
            double Need(double? propensity)
            {
                var ctx = BuildContext(propensity);
                return BehaviorMath.ComputeNeedState(ctx, new Dictionary<string, double>(), ctx.Snapshot.Behavior).NeedRest;
            }

            var unknown = Need(null);
            var gated = Need(-0.3);
            var atThreshold = Need(0.0);
            var pastThreshold = Need(0.02);

            Assert.AreEqual(unknown, gated, 1e-9, "A propensity far below the threshold (circadian gate) must not change NeedRest.");
            Assert.AreEqual(BehaviorMath.SleepPropensityNeedWeight * BehaviorMath.SleepOnsetLead, atThreshold - unknown, 1e-6);
            Assert.IsTrue(atThreshold - unknown >= 70.0, "Reaching the sleep-onset threshold must be enough to cross the sleep-prompt threshold of 70.");
            Assert.IsTrue(pastThreshold > atThreshold, "NeedRest keeps rising with propensity.");
        }

        [TestMethod]
        public void Character_SleepsNightly_On24And26HourDays()
        {
            foreach (var seed in new[] { 7, 21 })
            foreach (var hoursPerDay in new[] { 24, 26 })
            {
                var all = SimulateSessions(hoursPerDay, seed, out var alive);
                var sessions = all.Where(x => x.Hours >= 4.0).ToList(); // a sleep cut short by an ambush is not a night of its own
                var detail = string.Join("; ", all.Select(x => $"start {x.StartHour % hoursPerDay * 24.0 / hoursPerDay:F1}h(24h clock) abs {x.StartHour:F0} for {x.Hours:F1}h"));

                Assert.IsTrue(alive, $"seed {seed}, {hoursPerDay} h day: the character must survive {Days} days. {detail}");
                Assert.IsTrue(sessions.Count >= Days - 2, $"seed {seed}, {hoursPerDay} h day: expected about one sleep per night over {Days} days, got {sessions.Count}. {detail}");

                for (var i = 1; i < sessions.Count; i++)
                {
                    var gapDays = (sessions[i].StartHour - sessions[i - 1].StartHour) / hoursPerDay;
                    Assert.IsTrue(gapDays is > 0.6 and < 2.1, $"seed {seed}, {hoursPerDay} h day: gap between sleeps was {gapDays:F2} days. {detail}");
                }

                // After a full night (not the start-up sleep, not a catch-up after an ambush) bedtime falls around midnight.
                var afterFullNight = all.Skip(1).Where((x, i) => x.Hours >= 6.5 && all[i].Hours >= 6.5 && i > 0).ToList();
                foreach (var session in afterFullNight)
                {
                    var clock24 = session.StartHour % hoursPerDay * 24.0 / hoursPerDay;
                    Assert.IsTrue(clock24 >= 21.0 || clock24 <= 3.0, $"seed {seed}, {hoursPerDay} h day: went to sleep at {clock24:F1} on a 24-hour clock. {detail}");
                    Assert.IsTrue(session.Hours <= 11.5, $"seed {seed}, {hoursPerDay} h day: slept {session.Hours:F1} h. {detail}");
                }
            }
        }

        [TestMethod]
        public void Character_SleepsNightly_OnAnyDayLength_WhenTheBodyClockFollowsTheDay()
        {
            foreach (var hoursPerDay in new[] { 12, 20, 24, 26, 32, 40, 60 })
            {
                var all = SimulateSessions(hoursPerDay, 7, out var alive);
                var sessions = all.Where(x => x.Hours >= 4.0 * hoursPerDay / 24.0).ToList(); // an ambushed sleep is not a night of its own
                var detail = string.Join("; ", all.Select(x => $"start {x.StartHour % hoursPerDay * 24.0 / hoursPerDay:F1}h(24h clock) abs {x.StartHour:F0} for {x.Hours:F1}h"));

                Assert.IsTrue(alive, $"{hoursPerDay} h day: the character must survive {Days} days. {detail}");
                Assert.IsTrue(sessions.Count >= Days - 2, $"{hoursPerDay} h day: expected about one sleep per day over {Days} days, got {sessions.Count}. {detail}");

                for (var i = 1; i < sessions.Count; i++)
                {
                    var gapDays = (sessions[i].StartHour - sessions[i - 1].StartHour) / hoursPerDay;
                    Assert.IsTrue(gapDays is > 0.6 and < 2.1, $"{hoursPerDay} h day: gap between sleeps was {gapDays:F2} days. {detail}");
                }

                var slept = all.Sum(x => x.Hours) / (Days * hoursPerDay);
                Assert.IsTrue(slept is > 0.2 and < 0.42, $"{hoursPerDay} h day: slept {slept * 100:F0} % of the time. {detail}");

                var afterFullNight = all.Skip(1).Where((x, i) => x.Hours >= 6.5 * hoursPerDay / 24.0 && all[i].Hours >= 6.5 * hoursPerDay / 24.0 && i > 0).ToList();
                foreach (var session in afterFullNight)
                {
                    var clock24 = session.StartHour % hoursPerDay * 24.0 / hoursPerDay;
                    Assert.IsTrue(clock24 >= 21.0 || clock24 <= 3.0, $"{hoursPerDay} h day: went to sleep at {clock24:F1} on a 24-hour clock. {detail}");
                }
            }
        }

        [TestMethod]
        public void SettlersWithAnEarthBodyClock_OnA60HourDay_SleepMoreOftenThanOncePerDay()
        {
            // BodyClockFollowsDay = false: the body keeps its Earth time constants in real hours, so sleep pressure builds in ~16-25 h and
            // the character sleeps several times per 60 h day instead of once.
            var all = SimulateSessions(60, 7, out var alive, bodyClockFollowsDay: false);
            var sessions = all.Where(x => x.Hours >= 4.0).ToList();

            Assert.IsTrue(alive);
            Assert.IsTrue(sessions.Count > Days * 1.5, $"Got {sessions.Count} sleeps in {Days} days of 60 h with an Earth body clock.");
        }

        [TestMethod]
        public void Character_OnALongDay_StaysAliveAndNeverGoesMuchLongerThanADayWithoutSleep()
        {
            // Process S runs in real hours, so on a 40 h day sleep pressure builds up well before the day ends and the rhythm is
            // not once per day; it must still be a regular alternation of waking and sleeping.
            var all = SimulateSessions(40, 7, out var alive);
            var sessions = all.Where(x => x.Hours >= 4.0).ToList();
            var detail = string.Join("; ", all.Select(x => $"abs {x.StartHour:F0} for {x.Hours:F1}h"));

            Assert.IsTrue(alive, detail);
            Assert.IsTrue(sessions.Count >= Days - 1, $"Got {sessions.Count} sleeps in {Days} days of 40 h. {detail}");
            for (var i = 1; i < all.Count; i++)
                Assert.IsTrue(all[i].StartHour - (all[i - 1].StartHour + all[i - 1].Hours) <= 40.0, $"More than one day awake. {detail}");
        }

        [TestMethod]
        public void CharacterSavedMidSleep_StillWakesUpOnItsOwn_AndRecovers()
        {
            UseDayLength(24);
            var generator = ServiceProvider.GetRequiredService<IHumanBlueprintGenerator>();
            var factory = ServiceProvider.GetRequiredService<IHumanFactory>();
            var now = WDateTime.New(116, 1, 1, 0);
            var birth = now.Date.AddYears(-30);
            var human = factory.Create(generator.Generate(new HumanBlueprintRequest(
                Sex: SexBiology.Male, MinBirthDate: birth, MaxBirthDate: birth, Seed: 7)));
            var step = WTimeSpan.FromMinutes(15);

            // Run until the character falls asleep.
            var ticks = 0;
            while (human.Snapshot.Behavior.CurrentPlan?.Name != ActionNames.Sleep && ticks++ < 4 * 24 * 3)
            {
                human.Tick(now, step);
                var prompt = human.LastOutbox.OfType<SleepPromptRequested>().FirstOrDefault();
                if (prompt != null)
                    human.ReceiveEvent(new SleepConfirmed(now, prompt.Human, default));
                human.RestoreSnapshot(human.Snapshot with { Physiology = human.Snapshot.Physiology with { Hunger = 15, Thirst = 15 } });
                now += step;
            }

            Assert.AreEqual(ActionNames.Sleep, human.Snapshot.Behavior.CurrentPlan?.Name, "The character should have gone to sleep within three days.");
            var energyAtBedtime = human.Snapshot.Physiology.Energy;

            // Sleep for two hours, then "save and load" the snapshot.
            for (var i = 0; i < 8; i++)
            {
                human.Tick(now, step);
                now += step;
            }

            Assert.IsNotNull(human.Snapshot.Behavior.ActiveSleep, "The running session must be part of the snapshot.");
            human.RestoreSnapshot(human.Snapshot);

            var woke = false;
            for (var i = 0; i < 4 * 14 && !woke; i++)
            {
                human.Tick(now, step);
                woke = human.LastOutbox.OfType<SleepEnded>().Any();
                now += step;
            }

            Assert.IsTrue(woke, "After loading mid-sleep the character must still wake up (SleepEnded), not stay 'asleep' without a session.");
            Assert.IsNull(human.Snapshot.Behavior.ActiveSleep);
            Assert.AreNotEqual(ActionNames.Sleep, human.Snapshot.Behavior.CurrentPlan?.Name);
            Assert.IsTrue(human.Snapshot.Physiology.Energy > energyAtBedtime + 20, "The sleep must have restored energy.");
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
                new PhysiologyState(95, 0, 10, 10, 0, 0, 0, null) with { SleepPropensity = propensity },
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

        private List<Session> SimulateSessions(int hoursPerDay, int seed, out bool alive, bool bodyClockFollowsDay = true)
        {
            UseDayLength(hoursPerDay, bodyClockFollowsDay);
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
                // No world here, so keep the character fed. Restoring the snapshot every tick, asleep or not, also checks that a running
                // sleep session survives a save/load.
                human.RestoreSnapshot(human.Snapshot with { Physiology = human.Snapshot.Physiology with { Hunger = 15, Thirst = 15 } });

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
