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
    using GameEngineTools.World.Utils.Time;
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
            BehaviorConfig config)
        {
            if (committed.Human != ctx.Id || !IsLearnableAction(committed.ActionName))
            {
                return state;
            }

            var surface = ctx.Snapshot.InteractionSurface.Kind;
            var timeBand = ResolveTimeBand(committed.OccurredAt);
            var cue = ResolveCueKind(committed.ActionName, state, ctx);
            var key = BuildKey(committed.ActionName, surface, timeBand, cue);
            var traces = new Dictionary<string, BehaviorHabitTrace>(state.HabitTraces ?? new Dictionary<string, BehaviorHabitTrace>(), StringComparer.Ordinal);

            traces.TryGetValue(key, out var previous);
            var needFit = ComputeNeedFit(committed.ActionName, state, ctx);
            var coping = ComputeCopingReinforcement(committed.ActionName, state, ctx);
            var repetitionGain = previous is null ? 0.0 : Math.Min(0.25, previous.RepetitionCount * 0.015);
            var learning = Math.Clamp(config.HabitLearningRate, 0.0, 0.30) * (0.35 + (needFit * 0.65) + (coping * 0.70) + repetitionGain);
            var adaptiveReinforcement = Math.Clamp((previous?.AdaptiveReinforcement ?? 0.0) + (needFit * learning), 0.0, 1.0);
            var copingReinforcement = Math.Clamp((previous?.CopingReinforcement ?? 0.0) + (coping * learning), 0.0, 1.0);
            var strength = Math.Clamp(((previous?.Strength ?? 0.0) * 0.96) + learning, 0.0, 1.0);
            var tendency = ResolveTendency(strength, adaptiveReinforcement, copingReinforcement);

            traces[key] = new BehaviorHabitTrace(
                committed.ActionName,
                surface,
                timeBand,
                cue,
                strength,
                adaptiveReinforcement,
                copingReinforcement,
                (previous?.RepetitionCount ?? 0) + 1,
                committed.OccurredAt,
                tendency);

            return state with { HabitTraces = Trim(traces, config.MaxHabitTraces) };
        }

        public static IReadOnlyDictionary<string, BehaviorHabitTrace>? Decay(
            IReadOnlyDictionary<string, BehaviorHabitTrace>? traces,
            WTimeSpan dt,
            BehaviorConfig config)
        {
            if (traces is null || traces.Count == 0)
            {
                return traces;
            }

            var decay = Math.Max(0.0, config.HabitDecayPerDay) * Math.Max(0.0, dt.TotalDays);
            if (decay <= 0.0)
            {
                return traces;
            }

            var decayed = traces
                .Select(kv => new KeyValuePair<string, BehaviorHabitTrace>(
                    kv.Key,
                    kv.Value with { Strength = Math.Max(0.0, kv.Value.Strength - decay) }))
                .Where(kv => kv.Value.Strength >= 0.02)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

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
            var best = traces.Values
                .Where(trace => trace.ActionName == candidate.Name)
                .Select(trace => ComputeApplicability(trace, surface, timeBand, cue))
                .DefaultIfEmpty(0.0)
                .Max();

            return Math.Clamp(best, 0.0, 1.0);
        }

        #endregion Public API

        #region Cues

        private static HabitCueKind ResolveCueKind(string actionName, BehaviorState state, IHumanContext ctx)
        {
            var stressRelief = IsStressReliefCue(ctx);
            var needFit = ComputeNeedFit(actionName, state, ctx);

            if (stressRelief && ComputeCopingAffinity(actionName, state, ctx) > Math.Max(0.35, needFit))
            {
                return HabitCueKind.StressRelief;
            }

            if (actionName is Eat or Drink or SelfCare or MoveToRest && needFit >= 0.45)
            {
                return HabitCueKind.BodyNeed;
            }

            if (actionName is ReachOut or InviteIntimacy or MoveToSocial or MoveToPrivate && state.NeedBelonging >= 45)
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
            if (strength >= 0.18 && copingReinforcement > adaptiveReinforcement + 0.10)
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

        #region Bias

        private static double ComputeApplicability(
            BehaviorHabitTrace trace,
            SurfaceKind surface,
            HabitTimeBand timeBand,
            HabitCueKind cue)
        {
            var cueMatch = trace.CueKind == cue
                ? 1.0
                : trace.CueKind == HabitCueKind.Neutral || cue == HabitCueKind.Neutral ? 0.45 : 0.25;
            var surfaceMatch = trace.SurfaceKind == surface
                ? 1.0
                : trace.SurfaceKind == SurfaceKind.Unknown || surface == SurfaceKind.Unknown ? 0.55 : 0.32;
            var timeMatch = trace.TimeBand == timeBand ? 1.0 : 0.62;
            var tendencyScale = trace.Tendency switch
            {
                HabitTendency.MaladaptiveCoping when cue == HabitCueKind.StressRelief => 1.18,
                HabitTendency.Adaptive => 1.05,
                _ => 0.90
            };

            return Math.Clamp(trace.Strength * cueMatch * surfaceMatch * timeMatch * tendencyScale, 0.0, 1.0);
        }

        #endregion Bias

        #region Helpers

        private static string BuildKey(string actionName, SurfaceKind surface, HabitTimeBand timeBand, HabitCueKind cue)
            => $"action={actionName}|surface={surface}|time={timeBand}|cue={cue}";

        private static bool IsLearnableAction(string actionName)
            => actionName is not (GameEngineTools.Characters.Engines.ActionNames.Sleep or Flee or Fight);

        private static IReadOnlyDictionary<string, BehaviorHabitTrace>? Trim(Dictionary<string, BehaviorHabitTrace> traces, int maxHabitTraces)
        {
            var take = Math.Clamp(maxHabitTraces, 8, 512);
            var trimmed = traces
                .OrderByDescending(kv => kv.Value.Strength)
                .ThenByDescending(kv => kv.Value.RepetitionCount)
                .Take(take)
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

            return trimmed.Count == 0 ? null : trimmed;
        }

        private static double Clamp01(double value) => Math.Clamp(value, 0.0, 1.0);

        #endregion Helpers
    }
}
