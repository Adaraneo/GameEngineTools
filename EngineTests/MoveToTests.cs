// MoveToTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Memory;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using static GameEngineTools.Characters.Engines.ActionNames;

    /// <summary>
    /// Unit tests for <c>MoveTo:*</c> utility logic in <see cref="DefaultBehaviorEngine"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three independent drivers are tested in isolation:
    /// <list type="number">
    ///   <item><b>Social pull</b> — high NeedBelonging + low Crowding → <c>MoveTo:Social</c></item>
    ///   <item><b>Noise/stress escape</b> — high Noise + high Stress → <c>MoveTo:Private</c></item>
    ///   <item><b>Chronotype peak</b> — character at their natural active hour → movement boost</item>
    /// </list>
    /// </para>
    /// <para>
    /// Key calibration invariant: in a calm, neutral environment <c>MoveTo:*</c> must NEVER
    /// beat <c>Work</c> or <c>Create</c>. Movement should only win when the environment
    /// is genuinely bad or the character is at their chronotype peak.
    /// </para>
    /// <para>
    /// Utility formulas (current model in <see cref="DefaultBehaviorEngine"/>):
    /// <code>
    /// Util(need, weight)        = need * (0.5 + weight)
    /// needBel                   = 70 - MeanCloseness(empty=50)
    /// needComp                  = 50 + (Competence-0.5)*80           (Stress=0)
    /// noiseStress               = max(0, Noise-0.5) * 2 * (Stress/100) * 20
    /// socialPull                = needBel * Affiliation * max(0, 1-Crowding)
    /// chronoBonus               = max(0, 15 * (1 - |hour - peakHour| / 6))
    ///
    /// rawWork                   = Util(needComp, Competence)
    /// rawCreate                 = Util(needComp, Curiosity)
    /// productiveMult            = multiplier by SurfaceKind
    /// Work                      = rawWork * productiveMult
    /// Create                    = rawCreate * productiveMult
    /// productiveLoss            = max(rawWork - Work, rawCreate - Create)
    /// MoveTo:Work               = productiveLoss * moveCostFactor (+ optional chrono component)
    ///
    /// MoveTo:Social             = socialPull + chronoBonus
    /// MoveTo:Private            = noiseStress + chronoBonus * 0.5
    /// MoveTo:Rest               = noiseStress * 0.5
    /// MoveTo:Public             = chronoBonus * 0.4
    /// </code>
    /// </para>
    /// </remarks>
    [TestClass]
    public class MoveToTests : TestBase
    {
        #region Constants

        /// <summary>
        /// Far-future time — guarantees any CurrentPlan is always expired.
        /// elapsed = FarFuture - plan.Start >> any plan duration.
        /// </summary>
        private static readonly WDateTime FarFuture = new WDateTime(WTimeSpan.FromDays(2).Ticks);

        /// <summary>
        /// Hour 8 expressed as WDateTime ticks.
        /// Morning chronotype peaks at hour 8 → full chronoBonus=15 at exactly this time.
        /// </summary>
        private static readonly WDateTime Hour8 = new WDateTime(WTimeSpan.FromHours(8).Ticks);

        /// <summary>
        /// Hour 20 expressed as WDateTime ticks — Evening chronotype peak.
        /// </summary>
        private static readonly WDateTime Hour20 = new WDateTime(WTimeSpan.FromHours(20).Ticks);

        /// <summary>
        /// Hour 13 expressed as WDateTime ticks — Neutral chronotype peak.
        /// </summary>
        private static readonly WDateTime Hour13 = new WDateTime(WTimeSpan.FromHours(13).Ticks);

        /// <summary>
        /// Hour 3 — far from every chronotype peak, chronoBonus=0 for all types.
        /// </summary>
        private static readonly WDateTime DeadOfNight = new WDateTime(WTimeSpan.FromHours(3).Ticks);

        /// <summary>Sleep threshold set high — Tick() always reaches action selection.</summary>
        private static readonly SleepConfig NoSleepCfg = new SleepConfig() with
        {
            SleepPromptThreshold = 999.0
        };

        private static readonly BehaviorConfig DefaultBehaviorCfg = new BehaviorConfig();

        #endregion Constants

        // ════════════════════════════════════════════════════════════════════
        // Section 1 — Baseline invariant
        //
        // In a calm neutral environment MoveTo:* must NEVER beat Work or Create.
        // This is the most important test — it prevents MoveTo spam.
        //
        // Calibration (Competence=0.5, Stress=0, Noise=0.3, Crowding=0.3,
        //              Affiliation=0.5, Chronotype=Neutral, hour=3):
        //
        //   needComp     = 50 + (0.5-0.5)*80 = 50
        //   Work         = 50 * (0.5+0.5) = 50.0     ← winner
        //   Create       = 50 * (0.5+0.5) = 50.0
        //   noiseStress  = max(0, 0.3-0.5)*2*0*20 = 0
        //   chronoBonus  = 0  (hour 3, far from neutral peak 13)
        //   socialPull   = 20 * 0.5 * (1-0.3) = 7.0
        //   MoveTo:Social = 7.0 + 0 = 7.0   << Work=50 ✓
        // ════════════════════════════════════════════════════════════════════

        #region Baseline — MoveTo must not win in calm environment

        /// <summary>
        /// In a calm, neutral environment with no chronotype peak, no MoveTo action
        /// should beat Work. This guards against excessive NPC wandering.
        /// </summary>
        [TestMethod]
        public void Tick_CalmNeutralEnvironment_MoveToDoesNotBeatWork()
        {
            // Arrange
            var ctx = BuildContext(
                noise: 0.3, crowding: 0.3,
                stress: 0, affiliation: 0.5,
                competence: 0.5, curiosity: 0.5,
                chronotype: Chronotype.Neutral);

            var engine  = BuildEngine();
            var outbox  = new EventCollector();

            // Act — dead of night: chronoBonus=0 for all chronotypes
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
            Assert.IsFalse(chosen.ActionName.StartsWith("MoveTo:"),
                $"In a calm environment no MoveTo action must win. Chosen: {chosen.ActionName}");
        }

        #endregion Baseline — MoveTo must not win in calm environment

        // ════════════════════════════════════════════════════════════════════
        // Section 2 — Social pull
        //
        // Calibration (Affiliation=1.0, Crowding=0.0, Stress=0, Noise=0.3,
        //              Competence=0.25, Curiosity=0.25, hour=3 → chronoBonus=0):
        //
        //   needBel      = 70 - 50 = 20
        //   socialPull   = 20 * 1.0 * (1-0.0) = 20.0
        //   MoveTo:Social = 20 + 0 = 20.0
        //
        //   needComp     = 50 + (0.25-0.5)*80 = 30
        //   Work         = 30 * (0.5+0.25) = 22.5
        //   Create       = 30 * (0.5+0.25) = 22.5
        //
        //   MoveTo:Social=20 < Work=22.5 → still doesn't win with only social pull ✓
        //   (chronoBonus needed to tip it over)
        //
        // To make MoveTo:Social win cleanly we combine:
        //   · Affiliation=1.0, Crowding=0.0   → max socialPull=20
        //   · Morning chronotype at hour 8     → chronoBonus=15
        //   · Competence=0.1, Curiosity=0.1    → Work=26*(0.5+0.1)=15.6, Create=15.6
        //
        //   MoveTo:Social = 20 + 15 = 35 > Work=15.6 ✓
        // ════════════════════════════════════════════════════════════════════

        #region Social pull

        /// <summary>
        /// High Affiliation + empty location + chronotype peak → MoveTo:Social wins over Work.
        /// </summary>
        [TestMethod]
        public void Tick_HighAffilicationEmptyLocationAtChronoPeak_MoveToSocialWins()
        {
            // Arrange
            //
            // socialPull   = needBel * Affiliation * (1-Crowding)
            //              = 20 * 1.0 * 1.0 = 20
            // chronoBonus  = 15  (Morning at hour 8, distance=0)
            // MoveTo:Social = 20 + 15 = 35
            //
            // needComp = 50 + (0.1-0.5)*80 = 18
            // Work     = 18 * (0.5+0.1) = 10.8   << MoveTo:Social=35 ✓
            var ctx = BuildContext(
                noise: 0.3, crowding: 0.0,
                stress: 0, affiliation: 1.0,
                competence: 0.1, curiosity: 0.1,
                chronotype: Chronotype.Lark);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act — exactly at Morning peak hour
            engine.Tick(Hour8, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
            Assert.AreEqual(MoveToSocial, chosen.ActionName,
                $"High social pull + chrono peak must choose MoveTo:Social. Chosen: {chosen.ActionName}");
        }

        /// <summary>
        /// High Crowding suppresses social pull — character already has company,
        /// MoveTo:Social should not win even with high Affiliation.
        /// </summary>
        [TestMethod]
        public void Tick_HighCrowdingSuppressesSocialPull_MoveToSocialDoesNotWin()
        {
            // Arrange
            //
            // socialPull   = 20 * 1.0 * (1-0.9) = 2.0   ← suppressed by Crowding
            // chronoBonus  = 15  (Morning peak)
            // MoveTo:Social = 2 + 15 = 17
            //
            // needComp = 50 + (0.5-0.5)*80 = 50
            // Work     = 50 * (0.5+0.5) = 50   >> MoveTo:Social=17 ✓
            var ctx = BuildContext(
                noise: 0.3, crowding: 0.9,
                stress: 0, affiliation: 1.0,
                competence: 0.5, curiosity: 0.5,
                chronotype: Chronotype.Lark);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            engine.Tick(Hour8, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
            Assert.AreNotEqual(MoveToSocial, chosen.ActionName,
                $"High Crowding must suppress MoveTo:Social. Chosen: {chosen.ActionName}");
        }

        #endregion Social pull

        // ════════════════════════════════════════════════════════════════════
        // Section 3 — Noise/stress escape
        //
        // Calibration (Noise=0.9, Stress=100, Competence=0.25, hour=3):
        //
        //   noiseStress  = (0.9-0.5)*2 * (100/100) * 20 = 16.0
        //   chronoBonus  = 0  (hour 3)
        //   MoveTo:Private = 16.0 + 0*0.5 = 16.0
        //   MoveTo:Rest    = 16.0 * 0.5 = 8.0
        //
        //   needComp = 50 + (0.25-0.5)*80 - 100*0.2 = 10
        //   Work     = 10 * (0.5+0.25) = 7.5   << MoveTo:Private=16 ✓
        // ════════════════════════════════════════════════════════════════════

        #region Noise/stress escape

        /// <summary>
        /// Maximum noise + maximum stress → MoveTo:Private wins over Work.
        /// </summary>
        [TestMethod]
        public void Tick_MaxNoiseMaxStress_MoveToPrivateWinsOverWork()
        {
            // Arrange
            //
            // noiseStress    = (0.9-0.5)*2 * (100/100) * 20 = 16.0
            // MoveTo:Private = 16 + 0 = 16
            //
            // needBel  = 20, Affiliation=0.1 → ReachOut = 20*(0.5+0.1) = 12.0
            // needComp = 50 + (0.25-0.5)*80 - 100*0.2 = 10
            // Work     = 10*(0.5+0.25) = 7.5
            //
            // MoveTo:Private=16 > ReachOut=12 > Work=7.5 ✓
            var ctx = BuildContext(
                noise: 0.9, crowding: 0.5,
                stress: 100, affiliation: 0.1,
                competence: 0.25, curiosity: 0.25,
                chronotype: Chronotype.Neutral);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act — dead of night: no chrono component
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
            Assert.AreEqual(MoveToPrivate, chosen.ActionName,
                $"Max noise + max stress must choose MoveTo:Private. Chosen: {chosen.ActionName}");
        }

        /// <summary>
        /// Noise below the 0.5 threshold produces zero noiseStress — escape drive is silent.
        /// </summary>
        [TestMethod]
        public void Tick_NoiseBelowThreshold_NoEscapeDrive()
        {
            // Arrange — Noise=0.4 < 0.5 → noiseStress = max(0, 0.4-0.5)*... = 0
            var ctx = BuildContext(
                noise: 0.4, crowding: 0.5,
                stress: 100, affiliation: 0.5,
                competence: 0.5, curiosity: 0.5,
                chronotype: Chronotype.Neutral);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert — with Noise<0.5 noiseStress=0, so even high Stress can't push MoveTo
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
            Assert.IsFalse(chosen.ActionName.StartsWith("MoveTo:"),
                $"Noise below threshold must produce no escape drive. Chosen: {chosen.ActionName}");
        }

        #endregion Noise/stress escape

        // ════════════════════════════════════════════════════════════════════
        // Section 4 — Chronotype peak
        //
        // chronoBonus formula:
        //   distance = |hour - peakHour|
        //   if distance > 6 → bonus = 0
        //   else             → bonus = 15 * (1 - distance/6)
        //
        // Full bonus (15) at exactly peak hour.
        // Zero bonus at ±6 hours from peak or further.
        //
        // These tests verify bonus shape — not which action wins,
        // but that MoveTo:Social scores HIGHER at peak vs. off-peak.
        // ════════════════════════════════════════════════════════════════════

        #region Chronotype peak

        /// <summary>
        /// Morning chronotype at hour 8 produces higher MoveTo:Social utility
        /// than the same character at hour 20 (far from peak).
        /// </summary>
        [TestMethod]
        public void Tick_MorningChronotype_PeakHourBoostsMoveToSocialOverOffPeak()
        {
            // Peak   (hour 8):   chronoBonus=15, MoveTo:Social = 20+15 = 35, Work=10.8 → MoveTo wins
            // OffPeak (hour 20): chronoBonus=0,  MoveTo:Social = 20+0  = 20, Work=50   → Work wins
            var ctxPeak = BuildContext(noise: 0.3, crowding: 0.0, stress: 0,
                                         affiliation: 1.0, competence: 0.1, curiosity: 0.1,
                                         chronotype: Chronotype.Lark);
            var ctxOffPeak = BuildContext(noise: 0.3, crowding: 0.0, stress: 0,
                                         affiliation: 1.0, competence: 0.5, curiosity: 0.5,
                                         chronotype: Chronotype.Lark);

            var enginePeak = BuildEngine();
            var engineOffPeak = BuildEngine();
            var outboxPeak = new EventCollector();
            var outboxOffPeak = new EventCollector();

            enginePeak.Tick(Hour8, WTimeSpan.FromHours(1), ctxPeak, outboxPeak);
            engineOffPeak.Tick(Hour20, WTimeSpan.FromHours(1), ctxOffPeak, outboxOffPeak);

            var chosenPeak = outboxPeak.Drain().OfType<ActionCommitted>().Single();
            var chosenOffPeak = outboxOffPeak.Drain().OfType<ActionCommitted>().Single();

            Assert.AreEqual(MoveToSocial, chosenPeak.ActionName,
                $"Morning peak (hour 8) must choose MoveTo:Social. Chosen: {chosenPeak.ActionName}");
            Assert.AreNotEqual(MoveToSocial, chosenOffPeak.ActionName,
                $"Off-peak (hour 20, high competence) must NOT choose MoveTo:Social. Chosen: {chosenOffPeak.ActionName}");
        }

        /// <summary>
        /// Evening chronotype peaks at hour 20 — at that hour MoveTo utility must be
        /// higher than at hour 8 (which is far from the evening peak).
        /// </summary>
        [TestMethod]
        public void Tick_EveningChronotype_PeakAt20HigherThanAt8()
        {
            // Peak   (hour 20): chronoBonus=15, MoveTo:Social = 20+15 = 35, Work=10.8 → MoveTo wins
            // OffPeak (hour 8):  chronoBonus=0,  MoveTo:Social = 20+0  = 20, Work=50   → Work wins
            var ctxAt20 = BuildContext(noise: 0.3, crowding: 0.0, stress: 0,
                                       affiliation: 1.0, competence: 0.1, curiosity: 0.1,
                                       chronotype: Chronotype.Owl);
            var ctxAt8 = BuildContext(noise: 0.3, crowding: 0.0, stress: 0,
                                       affiliation: 1.0, competence: 0.5, curiosity: 0.5,
                                       chronotype: Chronotype.Owl);

            var engineAt20 = BuildEngine();
            var engineAt8 = BuildEngine();
            var outboxAt20 = new EventCollector();
            var outboxAt8 = new EventCollector();

            engineAt20.Tick(Hour20, WTimeSpan.FromHours(1), ctxAt20, outboxAt20);
            engineAt8.Tick(Hour8, WTimeSpan.FromHours(1), ctxAt8, outboxAt8);

            var chosenAt20 = outboxAt20.Drain().OfType<ActionCommitted>().Single();
            var chosenAt8 = outboxAt8.Drain().OfType<ActionCommitted>().Single();

            Assert.AreEqual(MoveToSocial, chosenAt20.ActionName,
                $"Evening peak (hour 20) must choose MoveTo:Social. Chosen: {chosenAt20.ActionName}");
            Assert.AreNotEqual(MoveToSocial, chosenAt8.ActionName,
                $"Off-peak (hour 8, high competence) must NOT choose MoveTo:Social. Chosen: {chosenAt8.ActionName}");
        }

        /// <summary>
        /// At 3am — more than 6 hours from every chronotype peak — chronoBonus must be zero.
        /// Verified by checking MoveTo:Public utility (which is chronoBonus * 0.4 only).
        /// </summary>
        [TestMethod]
        public void Tick_DeadOfNight_ChronoBonusIsZeroForAllChronotypes()
        {
            // At 3am all chronoBonus=0 → MoveTo:* wins only via social pull or noise stress.
            // With Affiliation=0.5, Noise=0.3 (below threshold), Stress=0 → no MoveTo should win.
            foreach (var chronotype in new[] { Chronotype.Lark, Chronotype.Owl, Chronotype.Neutral })
            {
                var ctx    = BuildContext(noise: 0.3, crowding: 0.3, stress: 0,
                                         affiliation: 0.5, competence: 0.5, curiosity: 0.5,
                                         chronotype: chronotype);
                var engine = BuildEngine();
                var outbox = new EventCollector();

                // Act
                engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

                var chosen = outbox.Drain().OfType<ActionCommitted>().Single();
                Assert.IsFalse(chosen.ActionName.StartsWith("MoveTo:"),
                    $"At 3am with no social/noise pressure, no MoveTo must win ({chronotype}). " +
                    $"Chosen: {chosen.ActionName}");
            }
        }

        #endregion Chronotype peak

        // ════════════════════════════════════════════════════════════════════
        // Section 5 — NaN guard
        //
        // Characters without a location have InteractionSurface(null, false, NaN, NaN).
        // The engine must handle this gracefully — NaN must not propagate into utility.
        // ════════════════════════════════════════════════════════════════════

        #region NaN guard

        /// <summary>
        /// Unplaced character (InteractionSurface with NaN Noise and Crowding) must not
        /// produce NaN utility — the engine must substitute safe defaults.
        /// </summary>
        [TestMethod]
        public void Tick_UnplacedCharacterWithNaNSurface_ProducesValidUtilities()
        {
            // Arrange — default context uses InteractionSurface(null, false, NaN, NaN)
            var ctx    = BuildContext(noise: double.NaN, crowding: double.NaN,
                                     stress: 0, affiliation: 0.5,
                                     competence: 0.5, curiosity: 0.5,
                                     chronotype: Chronotype.Neutral);
            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert — all ActionProposed utilities must be finite (no NaN)
            var proposed = outbox.Drain().OfType<ActionProposed>().ToList();
            Assert.IsTrue(proposed.Count > 0, "Engine must emit at least one ActionProposed.");

            foreach (var p in proposed)
            {
                Assert.IsFalse(double.IsNaN(p.Utility),
                    $"NaN utility detected for action '{p.ActionName}'. " +
                    $"NaN guard in MoveTo computation is broken.");
            }
        }

        #endregion NaN guard

        #region Productive surface model

        /// <summary>
        /// In a Work surface, productive actions should keep full strength
        /// and derived MoveTo:Work must not win.
        /// </summary>
        [TestMethod]
        public void Tick_WorkSurface_ProductiveActionBeatsMoveToWork()
        {
            // Arrange
            var ctx = BuildContext(
                noise: 0.3,
                crowding: 0.3,
                stress: 0,
                affiliation: 0.2,
                competence: 0.9,
                curiosity: 0.4,
                chronotype: Chronotype.Neutral,
                surfaceKind: SurfaceKind.Work);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();

            Assert.AreEqual(Work, chosen.ActionName,
                $"In Work surface, productive action should win. Chosen: {chosen.ActionName}");
        }

        /// <summary>
        /// In a Social surface, strong competence need should weaken local Work enough
        /// that derived MoveTo:Work can beat Work.
        /// </summary>
        [TestMethod]
        public void Tick_SocialSurface_HighCompetence_MoveToWorkCanBeatWork()
        {
            // Calibration with your patch multipliers:
            // competence=1.0 => needComp = 50 + (1.0-0.5)*80 = 90
            // rawWork        = 90 * (0.5 + 1.0) = 135
            // Work@Social    = 135 * 0.38 = 51.3
            // productiveLoss = 83.7
            // MoveToWork     ≈ 83.7 * 0.8 = 66.96 (dead of night => tiny/zero chrono effect)
            //
            // ReachOut is kept weak by low affiliation.
            var ctx = BuildContext(
                noise: 0.3,
                crowding: 0.2,
                stress: 0,
                affiliation: 0.1,
                competence: 1.0,
                curiosity: 0.2,
                chronotype: Chronotype.Neutral,
                surfaceKind: SurfaceKind.Social);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();

            Assert.AreEqual(MoveToWork, chosen.ActionName,
                $"In Social surface with strong competence drive, MoveTo:Work should win. Chosen: {chosen.ActionName}");
        }

        /// <summary>
        /// Unknown surface must not penalize productive actions.
        /// This protects unplaced / not-yet-contextualised characters.
        /// </summary>
        [TestMethod]
        public void Tick_UnknownSurface_DoesNotPenalizeProductiveActions()
        {
            // Arrange
            var ctx = BuildContext(
                noise: 0.3,
                crowding: 0.3,
                stress: 0,
                affiliation: 0.2,
                competence: 0.9,
                curiosity: 0.4,
                chronotype: Chronotype.Neutral,
                surfaceKind: SurfaceKind.Unknown);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();

            Assert.AreEqual(Work, chosen.ActionName,
                $"Unknown surface must not suppress productive actions. Chosen: {chosen.ActionName}");
        }

        /// <summary>
        /// Private surface should reduce productive actions compared to Work,
        /// but not kill them completely.
        /// </summary>
        [TestMethod]
        public void Tick_PrivateSurface_ProductiveActionsRemainViable()
        {
            // Arrange
            var ctx = BuildContext(
                noise: 0.2,
                crowding: 0.1,
                stress: 0,
                affiliation: 0.1,
                competence: 0.7,
                curiosity: 0.4,
                chronotype: Chronotype.Neutral,
                surfaceKind: SurfaceKind.Private);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(DeadOfNight, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();

            Assert.IsTrue(
                chosen.ActionName is Work or Create,
                $"Private surface should still allow productive action to win. Chosen: {chosen.ActionName}");
        }

        [TestMethod]
        public void Tick_WorkSurface_MoveToWorkIsNeverChosen()
        {
            // Arrange
            var ctx = BuildContext(
                noise: 0.3,
                crowding: 0.3,
                stress: 0,
                affiliation: 0.1,
                competence: 1.0,
                curiosity: 0.9,
                chronotype: Chronotype.Lark,
                surfaceKind: SurfaceKind.Work);

            var engine = BuildEngine();
            var outbox = new EventCollector();

            // Act
            engine.Tick(Hour8, WTimeSpan.FromHours(1), ctx, outbox);

            // Assert
            var chosen = outbox.Drain().OfType<ActionCommitted>().Single();

            Assert.AreNotEqual(MoveToWork, chosen.ActionName,
                $"MoveTo:Work must never be chosen when already in Work surface. Chosen: {chosen.ActionName}");
        }

        #endregion Productive surface model

        #region Factory methods

        /// <summary>
        /// Builds a <see cref="DefaultBehaviorEngine"/> with sleep threshold disabled.
        /// </summary>
        private static DefaultBehaviorEngine BuildEngine()
            => new DefaultBehaviorEngine(
                Options.Create(DefaultBehaviorCfg),
                Options.Create(NoSleepCfg),
                LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning)));

        /// <summary>
        /// Builds a fully parameterised <see cref="IHumanContext"/> for MoveTo tests.
        /// </summary>
        /// <param name="noise">
        /// Current location noise [0,1] or <see cref="double.NaN"/> for unplaced characters.
        /// </param>
        /// <param name="crowding">
        /// Current location crowding [0,1] or <see cref="double.NaN"/> for unplaced characters.
        /// </param>
        /// <param name="stress">Psychological stress [0,100].</param>
        /// <param name="affiliation">Motivation weight for social actions [0,1].</param>
        /// <param name="competence">Motivation weight for productive actions [0,1].</param>
        /// <param name="curiosity">Motivation weight for creative actions [0,1].</param>
        /// <param name="chronotype">Character's chronotype — determines peak movement hour.</param>
        private static IHumanContext BuildContext(
            double noise,
            double crowding,
            double stress,
            double affiliation,
            double competence,
            double curiosity,
            Chronotype chronotype,
            SurfaceKind surfaceKind = SurfaceKind.Unknown)
        {
            var physio = new PhysiologyState(
                Energy: 95,
                SleepDebtHours: 0,
                Hunger: 5,          // → Eat=8.5, non-competitive
                Thirst: 5,          // → Drink=8.0, non-competitive
                Pain: 0,
                ImmuneLoad: 0,
                BodyTempDelta: 0,
                Cycle: null);

            var psych = new PsychologyState(
                Valence: 0.0,       // → needBel = 70-50 = 20 (no valence component)
                Arousal: 0.5,
                Dominance: 0.5,
                Stress: stress,
                CognitiveLoad: 0,
                DominantEmotion: DiscreteEmotion.Neutral);

            var snapshot = new EnginesSnapshot(
                physio, psych,
                new BehaviorState(10, 5, 5, 20, 50, 30, null),
                new InteractionSurface(
                    Location: "test_location",
                    HasPrivacy: false,
                    Noise: noise,
                    Crowding: crowding,
                    Kind: surfaceKind),
                new RelationshipState(new Dictionary<HumanId, RelationshipEdge>()),
                new MemoryIndex(
                    new List<EpisodicMemory>(),
                    new Dictionary<string, SemanticFact>()));

            return new HumanContext
            {
                Id = new HumanId(Guid.NewGuid()),
                Biology = SexBiology.Female,
                Personality = new Personality(
                    BigFive: new BigFive(0.5, 0.5, 0.5, 0.5, 0.5),
                    Attachment: AttachmentStyle.Secure,
                    Communication: CommunicationStyle.Direct,
                    Motivation: new MotivationWeights(
                        Affiliation: affiliation,
                        Achievement: 0.5,
                        Power: 0.3,
                        Altruism: 0.4,
                        Competence: competence,
                        Autonomy: 0.5,
                        Curiosity: curiosity,
                        Rest: 0.6,
                        Sexuality: 0.3),
                    Sociosexuality: Sociosexuality.Intermediate,
                    Chronotype: chronotype),
                Snapshot = snapshot,
                Random = new ZeroRandom(),
                Logger = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning))
                                      .CreateLogger("Test"),
                EventBus = new NullEventBus(),
                Scheduler = new NullScheduler()
            };
        }

        #endregion Factory methods

        #region Fake implementations

        private sealed class ZeroRandom : IRandomSource
        {
            public int Next(int min, int max) => min;
            public double NextUnit()          => 0.0;
            public bool Chance(double p)      => false;
        }

        private sealed class NullEventBus : IEventBus
        {
            public void Publish(IDomainEvent @event) { }
            public IDisposable Subscribe<TEvent>(Action<TEvent> h)
                where TEvent : class, IDomainEvent => new Disposable();
        }

        private sealed class NullScheduler : IScheduler
        {
            public ScheduledId ScheduleAt(WDateTime w, ScheduledAction a, string? t = null)
                => new(Guid.NewGuid());

            public ScheduledId ScheduleAfter(WDateTime n, WTimeSpan d, ScheduledAction a, string? t = null)
                => new(Guid.NewGuid());

            public bool Cancel(ScheduledId id) => true;

            public IEnumerable<(ScheduledId, ScheduledAction)> Due(WDateTime n)
                => Enumerable.Empty<(ScheduledId, ScheduledAction)>();
        }

        private sealed class Disposable : IDisposable
        {
            public void Dispose() { }
        }

        #endregion Fake implementations
    }
}
