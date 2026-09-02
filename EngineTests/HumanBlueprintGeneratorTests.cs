// HumanBlueprintGeneratorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Utils.Time;

    [TestClass]
    public class HumanBlueprintGeneratorTests : TestBase
    {
        [TestMethod]
        public void Default_NowWithinFirstFewDaysOfEpoch_DoesNotThrow()
        {
            // Regression: a freshly started world clock sits at/near day 0 of the epoch.
            // HumanBlueprintSpec.Default previously subtracted up to 100 years of age from
            // "now" to build a birth-date window, and even its epoch-underflow fallback
            // (now.DayIndex - 30) could itself go negative that close to day 0, throwing an
            // unhandled ArgumentOutOfRangeException from WDateOnly's constructor.
            var now = new WDateOnly(5);

            var spec = HumanBlueprintSpec.Default(now);

            Assert.IsTrue(spec.DefaultMinBirthDate.DayIndex >= 0);
            Assert.IsTrue(spec.DefaultMaxBirthDate.DayIndex >= 0);
        }

        [TestMethod]
        public void Default_NowAtEpoch_ClampsBirthDatesToEpochFloor()
        {
            var now = new WDateOnly(0);

            var spec = HumanBlueprintSpec.Default(now);

            Assert.AreEqual(0, spec.DefaultMinBirthDate.DayIndex);
            Assert.AreEqual(0, spec.DefaultMaxBirthDate.DayIndex);
        }

        [TestMethod]
        public void Default_NowFarPastMaxAgeWindow_BehavesAsBefore()
        {
            // Sanity check that the clamp doesn't change behaviour once there's enough
            // calendar runway — same assertions that would have passed pre-fix.
            var now = WDateOnly.New(200, 1, 1);

            var spec = HumanBlueprintSpec.Default(now, minAgeYears: 18, maxAgeYears: 60);

            Assert.IsTrue(spec.DefaultMinBirthDate.DayIndex < spec.DefaultMaxBirthDate.DayIndex);
            Assert.IsTrue(spec.DefaultMaxBirthDate.DayIndex < now.DayIndex);
        }
    }
}
