// DefaultBereavementEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Bereavement
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Default <see cref="IBereavementEngine"/>. Registers a <see cref="LossRecord"/> on
    /// <see cref="BereavementOnset"/>, assigns a grief trajectory (weighted by the configured
    /// prevalences, attachment anxiety and violence of the death), and each tick advances the
    /// Dual-Process-Model oscillation — emitting <see cref="GriefPang"/> waves whose affect deltas
    /// Psychology applies. There is <b>no stage automaton</b>: grief oscillates between loss- and
    /// restoration-orientation under a declining envelope.
    /// </summary>
    internal sealed class DefaultBereavementEngine : IBereavementEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public BereavementState State { get; private set; } = BereavementState.Empty;

        /// <inheritdoc/>
        public BereavementConfig Config { get; }

        #endregion State and configuration

        private readonly ILogger _log;

        public DefaultBereavementEngine(IOptions<BereavementConfig> cfg, ILoggerFactory loggerFactory)
        {
            Config = cfg.Value;
            _log = loggerFactory.CreateLogger<DefaultBereavementEngine>();
        }

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            if (State.Losses.Count == 0)
                return;

            var days = Math.Max(0.0, dt.TotalDays);
            var avoidance = Math.Clamp(ctx.Personality.Attachment.Avoidance, 0.0, 1.0);
            var expressionMult = 1.0 - avoidance * Config.AvoidanceExpressionSuppression;

            var updated = new List<LossRecord>(State.Losses.Count);

            foreach (var loss in State.Losses)
            {
                var daysSinceOnset = Math.Max(0.0, WDateTime.Difference(now, loss.OnsetTime).TotalDays);

                // Decay grief intensity toward resolution at the trajectory-specific rate.
                var decay = BereavementMath.DecayPerDay(loss.Trajectory, Config) * days;
                var intensity = Math.Clamp(loss.GriefIntensity - decay, 0.0, 100.0);

                // Drop fully-resolved losses (a Prolonged trajectory decays so slowly it effectively persists).
                if (intensity <= Config.GriefResolvedThreshold)
                    continue;

                // Advance the DPM oscillation; a wave that re-enters loss-orientation fires a pang.
                var loRo = BereavementMath.LoRoOscillation(daysSinceOnset, Config);
                var crossedIntoLoss = loss.LoRoWeight <= Config.LoPhaseThreshold && loRo > Config.LoPhaseThreshold;

                if (crossedIntoLoss)
                {
                    var scale = intensity / 100.0 * expressionMult;
                    outbox.Add(new GriefPang(
                        now, ctx.Id, loss.DeceasedId, intensity,
                        ValenceDelta: -Config.GriefPangValenceDrop * scale,
                        MoodBaselineDelta: -Config.GriefPangMoodDrop * scale,
                        StressDelta: Config.GriefPangStress * scale));

                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultBereavementEngine), relatedPersonId: loss.DeceasedId.Value))
                    {
                        _log.LogDebug(
                            "[{Human}] grief pang for {Deceased}: intensity={Intensity:F1} trajectory={Trajectory}",
                            ctx.Id.Value, loss.DeceasedId.Value, intensity, loss.Trajectory);
                    }
                }

                updated.Add(loss with { GriefIntensity = intensity, LoRoWeight = loRo });
            }

            State = new BereavementState(updated);
        }

        #endregion IEngine — Tick

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case BereavementOnset onset when onset.Human == ctx.Id:
                    RegisterLoss(onset, ctx, outbox);
                    break;

                case FuneralHeld funeral when funeral.Human == ctx.Id:
                    ApplyFuneralRelief(funeral);
                    break;

                case Buried buried when buried.Human == ctx.Id:
                    MarkBuried(buried.Deceased);
                    break;

                case GraveVisited visit when visit.Human == ctx.Id:
                    ApplyGraveVisit(visit.Deceased);
                    break;
            }
        }

        private void MarkBuried(HumanId deceased)
        {
            var idx = IndexOf(deceased);
            if (idx < 0 || State.Losses[idx].Buried)
                return;

            var updated = State.Losses.ToList();
            updated[idx] = updated[idx] with { Buried = true };
            State = new BereavementState(updated);
        }

        private void ApplyGraveVisit(HumanId deceased)
        {
            var idx = IndexOf(deceased);
            if (idx < 0)
                return;

            var updated = State.Losses.ToList();
            var loss = updated[idx];
            updated[idx] = loss with
            {
                GriefIntensity = Math.Max(0.0, loss.GriefIntensity - Config.GraveVisitGriefRelief),
                // A tended grave internalises the bond as a secure base (adaptive; Field & Filanosky 2010).
                Bond = ContinuingBond.Internalized,
            };
            State = new BereavementState(updated);
        }

        private int IndexOf(HumanId deceased)
        {
            for (var i = 0; i < State.Losses.Count; i++)
                if (State.Losses[i].DeceasedId == deceased)
                    return i;
            return -1;
        }

        private void RegisterLoss(BereavementOnset onset, IHumanContext ctx, IEventCollector outbox)
        {
            // Idempotent: one loss record per deceased.
            if (State.Losses.Any(l => l.DeceasedId == onset.Deceased))
                return;

            var anxiety = ctx.Personality.Attachment.Anxiety;
            var violent = onset.Cause == DeathCause.Combat;
            var trajectory = BereavementMath.AssignTrajectory(ctx.Random, Config, anxiety, violent);
            var intensity = BereavementMath.OnsetIntensity(onset.BondStrength, onset.KinRole, Config);

            var record = new LossRecord(
                DeceasedId: onset.Deceased,
                KinRole: onset.KinRole,
                BondStrength: onset.BondStrength,
                OnsetTime: onset.OccurredAt,
                Trajectory: trajectory,
                GriefIntensity: intensity,
                LoRoWeight: 1.0, // acute loss-orientation immediately after the death
                Bond: ContinuingBond.None,
                Buried: false);

            State = new BereavementState(State.Losses.Append(record).ToList());

            outbox.Add(new GriefTrajectoryAssigned(onset.OccurredAt, ctx.Id, onset.Deceased, trajectory));

            // Acute grief spike — emitted as the first (largest) pang so Psychology applies the onset
            // affect drop through the same GriefPang path. Avoidance suppresses expressed magnitude only.
            var expressionMult = 1.0 - Math.Clamp(ctx.Personality.Attachment.Avoidance, 0.0, 1.0) * Config.AvoidanceExpressionSuppression;
            var onsetScale = intensity / 100.0 * expressionMult;
            outbox.Add(new GriefPang(
                onset.OccurredAt, ctx.Id, onset.Deceased, intensity,
                ValenceDelta: -Config.OnsetValenceDrop * onsetScale,
                MoodBaselineDelta: -Config.OnsetMoodBaselineDrop * onsetScale,
                StressDelta: Config.OnsetStressSpike * onsetScale));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultBereavementEngine), relatedPersonId: onset.Deceased.Value))
            {
                _log.LogInformation(
                    "[{Human}] bereavement onset for {Deceased}: bond={Bond:F0} kin={Kin} cause={Cause} → {Trajectory} intensity={Intensity:F0}",
                    ctx.Id.Value, onset.Deceased.Value, onset.BondStrength, onset.KinRole, onset.Cause, trajectory, intensity);
            }
        }

        private void ApplyFuneralRelief(FuneralHeld funeral)
        {
            var changed = false;
            var updated = new List<LossRecord>(State.Losses.Count);

            foreach (var loss in State.Losses)
            {
                if (loss.DeceasedId == funeral.Deceased)
                {
                    var relieved = Math.Max(
                        0.0,
                        loss.GriefIntensity - Config.FuneralGriefRelief
                            - loss.GriefIntensity * Config.FuneralIntensityReliefFraction);
                    updated.Add(loss with { GriefIntensity = relieved });
                    changed = true;
                }
                else
                {
                    updated.Add(loss);
                }
            }

            if (changed)
                State = new BereavementState(updated);
        }

        #endregion IEngine — Handle

        /// <inheritdoc/>
        public void RestoreState(BereavementState state) => State = state ?? BereavementState.Empty;
    }
}
