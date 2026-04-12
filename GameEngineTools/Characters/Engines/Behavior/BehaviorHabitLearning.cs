// BehaviorHabitLearning.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using static ActionNames;

    /// <summary>
    /// Deterministic habit acquisition and cue-matching formulas for behavior decisions.
    /// </summary>
    internal static class BehaviorHabitLearning
    {
        #region Public API

        public static BehaviorState LearnFromCommitment(
            BehaviorState state,
            ActionCommitted committed,
            IHumanContext ctx,
            BehaviorConfig config,
            ILogger? logger = null)
        {
            if (committed.Human != ctx.Id || !IsLearnableAction(committed.ActionName))
            {
                return state;
            }

            var signal = BuildLearningSignal(state, committed, ctx);
            var key = BuildKey(signal.ActionName, signal.SurfaceKind, signal.TimeBand, signal.CueKind);
            var traces = new Dictionary<string, BehaviorHabitTrace>(state.HabitTraces ?? new Dictionary<string, BehaviorHabitTrace>(), StringComparer.Ordinal);

            traces.TryGetValue(key, out var previous);
            var beforeStrength = previous?.Strength ?? 0.0;
            var repetitionGain = previous is null ? 0.0 : Math.Min(0.18, previous.RepetitionCount * 0.012);
            var learningQuality = Math.Clamp(
                0.18
                + signal.CueFit * 0.34
                + signal.ReliefFit * 0.36
                + signal.CopingFit * 0.24
                + repetitionGain
                - signal.ConstraintPenalty * 0.42,
                0.05,
                1.0);
            var learning = Math.Clamp(config.HabitLearningRate, 0.0, 0.30) * learningQuality;
            var adaptiveReinforcement = Math.Clamp((previous?.AdaptiveReinforcement ?? 0.0) + (signal.ReliefFit * signal.CueFit * learning), 0.0, 1.0);
            var copingReinforcement = Math.Clamp((previous?.CopingReinforcement ?? 0.0) + (signal.CopingFit * learning), 0.0, 1.0);
            var strength = Math.Clamp(((previous?.Strength ?? 0.0) * 0.985) + learning, 0.0, 1.0);
            var tendency = ResolveTendency(strength, adaptiveReinforcement, copingReinforcement);

            traces[key] = new BehaviorHabitTrace(
                signal.ActionName,
                signal.SurfaceKind,
                signal.TimeBand,
                signal.CueKind,
                strength,
                adaptiveReinforcement,
                copingReinforcement,
                (previous?.RepetitionCount ?? 0) + 1,
                signal.OccurredAt,
                tendency);

            LogHabitLearned(logger, ctx, signal, beforeStrength, strength, learning, tendency, (previous?.RepetitionCount ?? 0) + 1);

            return state with { HabitTraces = Trim(traces, config.MaxHabitTraces, committed.OccurredAt, state, ctx, logger) };
        }

        public static IReadOnlyDictionary<string, BehaviorHabitTrace>? Decay(
            IReadOnlyDictionary<string, BehaviorHabitTrace>? traces,
            WTimeSpan dt,
            BehaviorConfig config,
            IHumanContext? ctx = null,
            ILogger? logger = null)
        {
            if (traces is null || traces.Count == 0)
            {
                return traces;
            }

            var elapsedDays = Math.Max(0.0, dt.TotalDays);
            var decayPerDay = Math.Clamp(config.HabitDecayPerDay, 0.0, 0.95);
            if (elapsedDays <= 0.0 || decayPerDay <= 0.0)
            {
                return traces;
            }

            var retention = Math.Pow(1.0 - decayPerDay, elapsedDays);
            var reinforcementRetention = ComputeReinforcementRetention(decayPerDay, elapsedDays);
            var decayed = traces
                .Select(kv => new KeyValuePair<string, BehaviorHabitTrace>(
                    kv.Key,
                    DecayTrace(kv.Value, retention, reinforcementRetention)))
                .Where(kv => kv.Value.Strength >= 0.015 || kv.Value.RepetitionCount >= 3)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            LogHabitDecay(logger, ctx, elapsedDays, retention, traces.Count, decayed.Count);

            return decayed.Count == 0 ? null : decayed;
        }

        public static double ComputeCandidateBias(BehaviorContext context, BehaviorCandidate candidate)
        {
            var traces = context.State.HabitTraces;
            if (traces is null || traces.Count == 0)
            {
                return 0.0;
            }

            var surface = context.HumanContext.Snapshot.InteractionSurface.Kind;
            var timeBand = ResolveTimeBand(context.Now);
            var cue = ResolveCueKind(candidate.Name, context.State, context.HumanContext);
            var applicability = traces.Values
                .Where(trace => trace.ActionName == candidate.Name)
                .Select(trace => ComputeApplicability(trace, surface, timeBand, cue))
                .OrderByDescending(v => v)
                .Take(3)
                .ToArray();

            return AggregateApplicability(applicability);
        }

        public static double ComputeCandidateBias(BehaviorContext context, BehaviorCandidate candidate, IHabitApplicabilityModulator modulator)
        {
            var traces = context.State.HabitTraces;
            if (traces is null || traces.Count == 0)
            {
                return 0.0;
            }

            var surface = context.HumanContext.Snapshot.InteractionSurface.Kind;
            var timeBand = ResolveTimeBand(context.Now);
            var cue = ResolveCueKind(candidate.Name, context.State, context.HumanContext);
            var applicability = traces.Values
                .Where(trace => trace.ActionName == candidate.Name)
                .Select(trace =>
                {
                    var baseApplicability = ComputeApplicability(trace, surface, timeBand, cue);
                    return modulator.ModulateApplicability(context, candidate, trace, baseApplicability);
                })
                .OrderByDescending(v => v)
                .Take(3)
                .ToArray();

            return AggregateApplicability(applicability);
        }

        #endregion Public API

        #region Cues

        private static HabitCueKind ResolveCueKind(string actionName, BehaviorState state, IHumanContext ctx)
        {
            var stressRelief = IsStressReliefCue(ctx);
            var needFit = ComputeNeedFit(actionName, state, ctx);

            var copingAffinity = ComputeCopingAffinity(actionName, state, ctx);
            if (stressRelief && copingAffinity > Math.Max(0.35, needFit))
            {
                return HabitCueKind.StressRelief;
            }

            if (actionName is Eat or Drink or SelfCare or MoveToRest && needFit >= 0.45)
            {
                return HabitCueKind.BodyNeed;
            }

            if (actionName is MoveToPrivate && stressRelief && copingAffinity >= 0.42)
            {
                return HabitCueKind.StressRelief;
            }

            if (actionName is ReachOut or InviteIntimacy or MoveToSocial && state.NeedBelonging >= 45)
            {
                return HabitCueKind.SocialNeed;
            }

            if (actionName is MoveToPrivate && state.NeedBelonging >= 60 && ctx.Snapshot.Psychology.Stress < 45)
            {
                return HabitCueKind.SocialNeed;
            }

            if (actionName is Work or Create or MoveToWork && state.NeedCompetence >= 45)
            {
                return HabitCueKind.CompetenceNeed;
            }

            return HabitCueKind.Neutral;
        }

        private static HabitTimeBand ResolveTimeBand(WDateTime time)
            => time.Hour switch
            {
                >= 5 and < 11 => HabitTimeBand.Morning,
                >= 11 and < 17 => HabitTimeBand.Day,
                >= 17 and < 23 => HabitTimeBand.Evening,
                _ => HabitTimeBand.Night
            };

        private static bool IsStressReliefCue(IHumanContext ctx)
            => ctx.Snapshot.Psychology.Stress >= 55 || ctx.Snapshot.Psychology.Valence <= -0.25;

        #endregion Cues

        #region Reinforcement

        private static double ComputeNeedFit(string actionName, BehaviorState state, IHumanContext? ctx = null)
            => actionName switch
            {
                Eat => Clamp01(state.NeedFood / 100.0),
                Drink => Clamp01(state.NeedWater / 100.0),
                SelfCare => Clamp01(Math.Max(
                    Math.Max(state.NeedRest, Math.Max(state.NeedFood, state.NeedWater)) / 130.0,
                    ctx is null ? 0.0 : BehaviorMath.ComputeSelfCareNeed(ctx.Snapshot.Physiology) / 100.0)),
                MoveToRest => Clamp01(state.NeedRest / 100.0),
                ReachOut or MoveToSocial => Clamp01(state.NeedBelonging / 100.0),
                InviteIntimacy => Clamp01(state.NeedIntimacy / 100.0),
                Work or Create or MoveToWork => Clamp01(state.NeedCompetence / 100.0),
                Idle => Clamp01(state.NeedRest / 140.0),
                _ => 0.20
            };

        private static double ComputeCopingReinforcement(string actionName, BehaviorState state, IHumanContext ctx)
        {
            if (!IsStressReliefCue(ctx))
            {
                return 0.0;
            }

            var affinity = ComputeCopingAffinity(actionName, state, ctx);
            var needFit = ComputeNeedFit(actionName, state, ctx);
            return Math.Clamp(affinity * (1.0 - (needFit * 0.65)), 0.0, 1.0);
        }

        private static double ComputeCopingAffinity(string actionName, BehaviorState state, IHumanContext ctx)
        {
            var coping = ctx.PsychologyProfile?.Coping ?? PsychologicalProfile.Default.Coping;
            var avoidantBoost = coping == CopingStyle.Avoidant ? 0.20 : 0.0;
            var peoplePleasingBoost = coping == CopingStyle.PeoplePleasing ? 0.14 : 0.0;
            var rationalizingBoost = coping == CopingStyle.Rationalizing ? 0.16 : 0.0;

            return actionName switch
            {
                Idle => 0.70 + avoidantBoost,
                MoveToPrivate or MoveToRest => 0.58 + avoidantBoost,
                SelfCare => state.NeedRest < 35 ? 0.55 + avoidantBoost : 0.24,
                Eat => state.NeedFood < 35 ? 0.42 : 0.10,
                Drink => state.NeedWater < 35 ? 0.28 : 0.08,
                Work or Create => state.NeedCompetence < 45 ? 0.45 + rationalizingBoost : 0.12,
                ReachOut => state.NeedBelonging < 35 ? 0.35 + peoplePleasingBoost : 0.12,
                InviteIntimacy => state.NeedIntimacy < 35 ? 0.32 + peoplePleasingBoost : 0.10,
                _ => 0.0
            };
        }

        private static HabitTendency ResolveTendency(double strength, double adaptiveReinforcement, double copingReinforcement)
        {
            if (strength >= 0.12 && copingReinforcement > adaptiveReinforcement + 0.08)
            {
                return HabitTendency.MaladaptiveCoping;
            }

            if (strength >= 0.15 && adaptiveReinforcement >= copingReinforcement + 0.08)
            {
                return HabitTendency.Adaptive;
            }

            return HabitTendency.Neutral;
        }

        #endregion Reinforcement

        #region Learning signal

        private static HabitLearningSignal BuildLearningSignal(BehaviorState state, ActionCommitted committed, IHumanContext ctx)
        {
            var surface = ctx.Snapshot.InteractionSurface.Kind;
            var timeBand = ResolveTimeBand(committed.OccurredAt);
            var cue = ResolveCueKind(committed.ActionName, state, ctx);
            var reliefFit = ComputeNeedFit(committed.ActionName, state, ctx);
            var coping = ComputeCopingReinforcement(committed.ActionName, state, ctx);
            var cueFit = ComputeCueFit(committed.ActionName, cue, reliefFit, coping);
            var constraintPenalty = ComputeConstraintPenalty(committed, reliefFit, coping);

            return new HabitLearningSignal(
                committed.ActionName,
                surface,
                timeBand,
                cue,
                cueFit,
                reliefFit,
                coping,
                constraintPenalty,
                committed.OccurredAt);
        }

        private static double ComputeCueFit(string actionName, HabitCueKind cue, double reliefFit, double coping)
        {
            var expectedCue = actionName switch
            {
                Eat or Drink or SelfCare or MoveToRest => HabitCueKind.BodyNeed,
                ReachOut or InviteIntimacy or MoveToSocial or MoveToPrivate => HabitCueKind.SocialNeed,
                Work or Create or MoveToWork => HabitCueKind.CompetenceNeed,
                Idle => HabitCueKind.StressRelief,
                _ => HabitCueKind.Neutral
            };

            if (cue == expectedCue)
            {
                return Math.Clamp(0.65 + Math.Max(reliefFit, coping) * 0.35, 0.0, 1.0);
            }

            if (cue == HabitCueKind.StressRelief && coping > 0.0)
            {
                return Math.Clamp(0.45 + coping * 0.45, 0.0, 0.90);
            }

            if (cue == HabitCueKind.Neutral)
            {
                return Math.Clamp(0.20 + reliefFit * 0.35, 0.0, 0.55);
            }

            return Math.Clamp(0.15 + reliefFit * 0.30, 0.0, 0.50);
        }

        private static double ComputeConstraintPenalty(ActionCommitted committed, double reliefFit, double coping)
        {
            var conflictPenalty = string.IsNullOrWhiteSpace(committed.ConflictReason) ? 0.0 : 0.35;
            var intentMismatchPenalty = !string.IsNullOrWhiteSpace(committed.IntendedActionName)
                && !string.Equals(committed.IntendedActionName, committed.ActionName, StringComparison.Ordinal)
                    ? 0.35
                    : 0.0;
            var lowFitPenalty = Math.Max(0.0, 0.35 - Math.Max(reliefFit, coping));

            return Math.Clamp(conflictPenalty + intentMismatchPenalty + lowFitPenalty, 0.0, 1.0);
        }

        #endregion Learning signal

        #region Bias

        private static double ComputeApplicability(
            BehaviorHabitTrace trace,
            SurfaceKind surface,
            HabitTimeBand timeBand,
            HabitCueKind cue)
        {
            var cueMatch = ComputeCueSimilarity(trace.CueKind, cue);
            var surfaceMatch = ComputeSurfaceSimilarity(trace.SurfaceKind, surface);
            var timeMatch = ComputeTimeSimilarity(trace.TimeBand, timeBand);
            var tendencyScale = trace.Tendency switch
            {
                HabitTendency.MaladaptiveCoping when cue == HabitCueKind.StressRelief => 1.18,
                HabitTendency.Adaptive => 1.05,
                _ => 0.90
            };

            return Math.Clamp(trace.Strength * cueMatch * surfaceMatch * timeMatch * tendencyScale, 0.0, 1.0);
        }

        private static double ComputeCueSimilarity(HabitCueKind learned, HabitCueKind current)
        {
            if (learned == current)
            {
                return 1.0;
            }

            if (learned == HabitCueKind.Neutral || current == HabitCueKind.Neutral)
            {
                return 0.42;
            }

            if ((learned == HabitCueKind.BodyNeed && current == HabitCueKind.StressRelief)
                || (learned == HabitCueKind.StressRelief && current == HabitCueKind.BodyNeed))
            {
                return 0.34;
            }

            if ((learned == HabitCueKind.SocialNeed && current == HabitCueKind.StressRelief)
                || (learned == HabitCueKind.StressRelief && current == HabitCueKind.SocialNeed))
            {
                return 0.28;
            }

            return 0.16;
        }

        private static double ComputeSurfaceSimilarity(SurfaceKind learned, SurfaceKind current)
        {
            if (learned == current)
            {
                return 1.0;
            }

            if (learned == SurfaceKind.Unknown || current == SurfaceKind.Unknown)
            {
                return 0.50;
            }

            if ((learned == SurfaceKind.Private && current == SurfaceKind.Rest)
                || (learned == SurfaceKind.Rest && current == SurfaceKind.Private)
                || (learned == SurfaceKind.Social && current == SurfaceKind.Public)
                || (learned == SurfaceKind.Public && current == SurfaceKind.Social)
                || (learned == SurfaceKind.Work && current == SurfaceKind.Public)
                || (learned == SurfaceKind.Public && current == SurfaceKind.Work))
            {
                return 0.48;
            }

            return 0.22;
        }

        private static double ComputeTimeSimilarity(HabitTimeBand learned, HabitTimeBand current)
        {
            if (learned == current)
            {
                return 1.0;
            }

            var distance = Math.Abs((int)learned - (int)current);
            distance = Math.Min(distance, 4 - distance);
            return distance == 1 ? 0.72 : 0.38;
        }

        #endregion Bias

        #region Helpers

        private static string BuildKey(string actionName, SurfaceKind surface, HabitTimeBand timeBand, HabitCueKind cue)
            => $"action={actionName}|surface={surface}|time={timeBand}|cue={cue}";

        private static bool IsLearnableAction(string actionName)
            => actionName is not (GameEngineTools.Characters.Engines.ActionNames.Sleep or Flee or Fight);

        private static IReadOnlyDictionary<string, BehaviorHabitTrace>? Trim(
            Dictionary<string, BehaviorHabitTrace> traces,
            int maxHabitTraces,
            WDateTime now,
            BehaviorState state,
            IHumanContext ctx,
            ILogger? logger = null)
        {
            var take = Math.Clamp(maxHabitTraces, 8, 512);
            var trimmed = traces
                .OrderByDescending(kv => ComputeRetentionScore(kv.Value, now, state, ctx))
                .ThenByDescending(kv => kv.Value.Strength)
                .ThenByDescending(kv => kv.Value.RepetitionCount)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(take)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            if (trimmed.Count < traces.Count)
            {
                LogHabitPruned(logger, ctx, traces.Count, trimmed.Count, take);
            }

            return trimmed.Count == 0 ? null : trimmed;
        }

        private static double ComputeRetentionScore(BehaviorHabitTrace trace, WDateTime now, BehaviorState state, IHumanContext ctx)
        {
            var ageDays = Math.Max(0.0, (now - trace.LastUpdatedAt).TotalDays);
            var recency = 1.0 / (1.0 + ageDays / 14.0);
            var currentCue = ResolveCueKind(trace.ActionName, state, ctx);
            var applicability = ComputeApplicability(trace, ctx.Snapshot.InteractionSurface.Kind, ResolveTimeBand(now), currentCue);
            var repetition = Math.Clamp(Math.Log(1.0 + trace.RepetitionCount) / Math.Log(16.0), 0.0, 1.0);

            return trace.Strength * 0.55 + recency * 0.20 + applicability * 0.15 + repetition * 0.10;
        }

        private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

        private static BehaviorHabitTrace DecayTrace(
            BehaviorHabitTrace trace,
            double strengthRetention,
            double reinforcementRetention)
            => trace with
            {
                Strength = Clamp01(trace.Strength * strengthRetention),
                AdaptiveReinforcement = Clamp01(trace.AdaptiveReinforcement * reinforcementRetention),
                CopingReinforcement = Clamp01(trace.CopingReinforcement * reinforcementRetention)
            };

        private static double ComputeReinforcementRetention(double decayPerDay, double elapsedDays)
        {
            var reinforcementDecay = Math.Clamp(decayPerDay * 0.45, 0.0, 0.95);
            return Math.Pow(1.0 - reinforcementDecay, elapsedDays);
        }

        private static double AggregateApplicability(IReadOnlyList<double> applicability)
        {
            if (applicability.Count == 0)
            {
                return 0.0;
            }

            var total = 0.0;
            var weights = new[] { 1.0, 0.45, 0.20 };
            for (var i = 0; i < applicability.Count && i < weights.Length; i++)
            {
                total += Math.Clamp(applicability[i], 0.0, 1.0) * weights[i];
            }

            return Clamp01(total);
        }

        private static void LogHabitLearned(
            ILogger? logger,
            IHumanContext ctx,
            HabitLearningSignal signal,
            double beforeStrength,
            double afterStrength,
            double learning,
            HabitTendency tendency,
            int repetitionCount)
        {
            if (logger is null)
            {
                return;
            }

            using (logger.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(BehaviorHabitLearning))))
            {
                logger.BehaviorHabitLearned(
                    ctx.Id.Value.ToString(),
                    signal.ActionName,
                    signal.CueKind.ToString(),
                    signal.SurfaceKind.ToString(),
                    signal.TimeBand.ToString(),
                    beforeStrength,
                    afterStrength,
                    learning,
                    signal.CueFit,
                    signal.ReliefFit,
                    signal.CopingFit,
                    signal.ConstraintPenalty,
                    tendency.ToString(),
                    repetitionCount);
            }
        }

        private static void LogHabitDecay(
            ILogger? logger,
            IHumanContext? ctx,
            double elapsedDays,
            double retention,
            int beforeCount,
            int afterCount)
        {
            if (logger is null || ctx is null)
            {
                return;
            }

            using (logger.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(BehaviorHabitLearning))))
            {
                logger.BehaviorHabitDecayed(
                    ctx.Id.Value.ToString(),
                    elapsedDays,
                    retention,
                    beforeCount,
                    afterCount,
                    beforeCount - afterCount);
            }
        }

        private static void LogHabitPruned(ILogger? logger, IHumanContext ctx, int beforeCount, int afterCount, int maxTraces)
        {
            if (logger is null)
            {
                return;
            }

            using (logger.BeginScope(new CharacterLogScope(ctx.Id.Value, nameof(BehaviorHabitLearning))))
            {
                logger.BehaviorHabitPruned(ctx.Id.Value.ToString(), beforeCount, afterCount, maxTraces);
            }
        }

        #endregion Helpers
    }
}
