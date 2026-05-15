// DefaultGoalEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Goals
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.Psychology;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.Logging;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;
    using static GameEngineTools.Characters.Engines.ActionNames;

    internal sealed class DefaultGoalEngine : IGoalEngine
    {
        #region State and configuration

        /// <inheritdoc/>
        public GoalState State { get; private set; }

        /// <inheritdoc/>
        public GoalConfig Config { get; }

        #endregion

        #region Construction

        private readonly ILogger _log;

        public DefaultGoalEngine(IOptions<GoalConfig> cfg, ILogger<DefaultGoalEngine> log)
        {
            Config = cfg.Value;
            _log = log;
            State = GoalState.Empty;
        }

        #endregion

        #region IGoalEngine — SeedFromPersonality

        /// <inheritdoc/>
        public void SeedFromPersonality(Personality personality, WDateTime now)
        {
            var goals = State.Goals.ToList();

            if (personality.Motivation.Competence > Config.MasterCraftCompetenceThreshold
                && !goals.Any(g => g.Kind == PersistentGoalKind.MasterCraft && g.Resolution is null))
            {
                var goal = BuildGoal(PersistentGoalKind.MasterCraft, GoalOrigin.Personality, Config.PersonalitySeedSalience, now);
                goals.Add(goal);
                LogSeeded(goal);
            }

            if (personality.Motivation.Affiliation > Config.FindPartnerAffiliationThreshold
                && !goals.Any(g => g.Kind == PersistentGoalKind.FindPartner && g.Resolution is null))
            {
                var goal = BuildGoal(PersistentGoalKind.FindPartner, GoalOrigin.Personality, Config.PersonalitySeedSalience, now);
                goals.Add(goal);
                LogSeeded(goal);
            }

            if (personality.BigFive.Openness > Config.FindMeaningOpennessThreshold
                && !goals.Any(g => g.Kind == PersistentGoalKind.FindMeaning && g.Resolution is null))
            {
                var goal = BuildGoal(PersistentGoalKind.FindMeaning, GoalOrigin.Personality, Config.PersonalitySeedSalience, now);
                goals.Add(goal);
                LogSeeded(goal);
            }

            State = new GoalState(goals);
        }

        #endregion

        #region IEngine — Tick

        /// <inheritdoc/>
        public void Tick(WDateTime now, WTimeSpan dt, IHumanContext ctx, IEventCollector outbox)
        {
            var days = Math.Max(0.0, dt.TotalDays);
            if (days <= 0.0 || State.Goals.Count == 0)
            {
                return;
            }

            var updated = new List<PersistentGoal>(State.Goals.Count);

            foreach (var goal in State.Goals)
            {
                if (goal.Resolution is not null)
                {
                    updated.Add(goal);
                    continue;
                }

                var oldSalience = goal.Salience;
                var oldProgress = goal.Progress;

                // 1. Decay salience
                var decayMultiplier = 1.0;
                var daysSinceProgress = Math.Max(0.0, (now - goal.LastProgressAt).TotalDays);
                if (daysSinceProgress > Config.NegligenceThresholdDays)
                {
                    decayMultiplier = Config.NegligenceDecayMultiplier;
                }

                var newSalience = Clamp01(goal.Salience - Config.SalienceDecayPerDay * days * decayMultiplier);

                // 2. Decay frustration
                var newFrustration = Clamp01(goal.Frustration - Config.FrustrationDecayPerDay * days);

                var mutated = goal with
                {
                    Salience = newSalience,
                    Frustration = newFrustration
                };

                // 3. Check resolutions
                GoalResolution? resolution = null;
                if (mutated.Progress >= 1.0)
                    resolution = GoalResolution.Completed;
                else if (mutated.Frustration >= Config.AbandonmentFrustrationThreshold)
                    resolution = GoalResolution.Abandoned;
                else if (mutated.Salience <= Config.FadedSalienceThreshold)
                    resolution = GoalResolution.Faded;

                if (resolution is not null)
                {
                    mutated = mutated with { Resolution = resolution };
                    EmitGoalResolved(now, ctx.Id, mutated, outbox);
                }
                else if (Math.Abs(newSalience - oldSalience) > 0.05 || Math.Abs(mutated.Progress - oldProgress) > 0.05)
                {
                    outbox.Add(new GoalProgressed(now, ctx.Id, mutated.Id, mutated.Kind,
                        oldSalience, newSalience, oldProgress, mutated.Progress));

                    using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultGoalEngine)))
                    {
                        _log.GoalProgressed(ctx.Id.Value.ToString(), mutated.Kind.ToString(),
                            oldSalience, newSalience, oldProgress, mutated.Progress);
                    }
                }

                updated.Add(mutated);
            }

            State = new GoalState(updated);
        }

        #endregion

        #region IEngine — Handle

        /// <inheritdoc/>
        public void Handle(IDomainEvent @event, IHumanContext ctx, IEventCollector outbox)
        {
            switch (@event)
            {
                case ActionCommitted ac when ac.Human == ctx.Id:
                    HandleActionCommitted(ac, ctx, outbox);
                    break;

                case GoalInjected inj when inj.Human == ctx.Id:
                    HandleGoalInjected(inj, ctx, outbox);
                    break;

                case InteractionOutcome io when !io.Accepted && io.To == ctx.Id:
                    HandleRejectedInteraction(io, ctx);
                    break;

                case ChildBorn cb when cb.ParentA == ctx.Id || cb.ParentB == ctx.Id:
                    HandleChildBorn(cb, ctx, outbox);
                    break;

                case StressManifested sm when sm.Human == ctx.Id
                    && sm.Manifestation.Contains("trauma", StringComparison.OrdinalIgnoreCase):
                    HandleTrauma(sm, ctx, outbox);
                    break;

                case SexualEncounterOutcome seo when seo.Accepted
                    && (seo.From == ctx.Id || seo.To == ctx.Id):
                    HandleSexualEncounterAccepted(seo, ctx, outbox);
                    break;
            }
        }

        #endregion

        #region IEngine — RestoreState

        /// <inheritdoc/>
        public void RestoreState(GoalState state) => State = state;

        #endregion

        #region Private helpers

        private void HandleActionCommitted(ActionCommitted ac, IHumanContext ctx, IEventCollector outbox)
        {
            if (State.Goals.Count == 0) return;

            var goals = State.Goals.ToList();
            var changed = false;

            for (var i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (goal.Resolution is not null) continue;

                var (strong, weak) = IsGoalRelevantAction(goal, ac);
                if (!strong && !weak) continue;

                var progressGain = strong ? Config.ProgressGainStrong : Config.ProgressGainWeak;
                var updated = goal with
                {
                    Salience = Clamp01(goal.Salience + Config.SalienceGainOnProgress),
                    Progress = Clamp01(goal.Progress + progressGain),
                    LastProgressAt = ac.OccurredAt
                };

                goals[i] = updated;
                changed = true;

                if (updated.Progress >= 1.0)
                {
                    goals[i] = updated with { Resolution = GoalResolution.Completed };
                    EmitGoalResolved(ac.OccurredAt, ctx.Id, goals[i], outbox);
                }
            }

            if (changed)
            {
                State = new GoalState(goals);
            }
        }

        private void HandleGoalInjected(GoalInjected inj, IHumanContext ctx, IEventCollector outbox)
        {
            var goal = new PersistentGoal(
                Guid.NewGuid(),
                inj.Kind,
                GoalOrigin.Scripted,
                Clamp01(inj.InitialSalience),
                0.0,
                0.0,
                inj.OccurredAt,
                inj.OccurredAt,
                inj.TargetHuman);

            AddGoal(goal, ctx, outbox);
        }

        private void HandleRejectedInteraction(InteractionOutcome io, IHumanContext ctx)
        {
            var goals = State.Goals.ToList();
            var changed = false;

            for (var i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (goal.Resolution is not null) continue;
                if (goal.Kind != PersistentGoalKind.FindPartner && goal.Kind != PersistentGoalKind.RepairRelationship) continue;

                if (goal.Kind == PersistentGoalKind.RepairRelationship
                    && goal.TargetHuman is not null
                    && goal.TargetHuman != io.From)
                {
                    continue;
                }

                goals[i] = goal with
                {
                    Frustration = Clamp01(goal.Frustration + Config.FrustrationGainOnBlock)
                };
                changed = true;
            }

            if (changed)
            {
                State = new GoalState(goals);
            }
        }

        private void HandleChildBorn(ChildBorn cb, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsGoalActive(PersistentGoalKind.ProtectFamily))
            {
                var goal = new PersistentGoal(
                    Guid.NewGuid(),
                    PersistentGoalKind.ProtectFamily,
                    GoalOrigin.Event,
                    0.8,
                    0.0,
                    0.0,
                    cb.OccurredAt,
                    cb.OccurredAt);

                AddGoal(goal, ctx, outbox);
            }
        }

        private void HandleTrauma(StressManifested sm, IHumanContext ctx, IEventCollector outbox)
        {
            if (!IsGoalActive(PersistentGoalKind.OvercomeTrauma))
            {
                var goal = BuildGoal(PersistentGoalKind.OvercomeTrauma, GoalOrigin.Event, 0.5, sm.OccurredAt);
                AddGoal(goal, ctx, outbox);
            }
        }

        private void HandleSexualEncounterAccepted(SexualEncounterOutcome seo, IHumanContext ctx, IEventCollector outbox)
        {
            var goals = State.Goals.ToList();
            var changed = false;

            for (var i = 0; i < goals.Count; i++)
            {
                var goal = goals[i];
                if (goal.Resolution is not null) continue;
                if (goal.Kind != PersistentGoalKind.FindPartner) continue;

                var updated = goal with
                {
                    Progress = Clamp01(goal.Progress + 0.3),
                    Salience = Clamp01(goal.Salience + 0.1),
                    LastProgressAt = seo.OccurredAt
                };

                if (updated.Progress >= 1.0)
                {
                    updated = updated with { Resolution = GoalResolution.Completed };
                    goals[i] = updated;
                    EmitGoalResolved(seo.OccurredAt, ctx.Id, updated, outbox);
                }
                else
                {
                    goals[i] = updated;
                }

                changed = true;
            }

            if (changed)
            {
                State = new GoalState(goals);
            }
        }

        private bool IsGoalActive(PersistentGoalKind kind)
            => State.Active.Any(g => g.Kind == kind);

        private void AddGoal(PersistentGoal goal, IHumanContext ctx, IEventCollector outbox)
        {
            var goals = State.Goals.ToList();
            goals.Add(goal);
            State = new GoalState(goals);

            outbox.Add(new GoalActivated(goal.CreatedAt, ctx.Id, goal.Id, goal.Kind, goal.Origin, goal.Salience));

            using (_log.BeginCharacterScope(ctx.Id.Value, nameof(DefaultGoalEngine)))
            {
                _log.GoalActivated(ctx.Id.Value.ToString(), goal.Kind.ToString(), goal.Origin.ToString(), goal.Salience);
            }
        }

        private void EmitGoalResolved(WDateTime now, HumanId humanId, PersistentGoal goal, IEventCollector outbox)
        {
            outbox.Add(new GoalResolved(now, humanId, goal.Id, goal.Kind, goal.Resolution!.Value));

            using (_log.BeginCharacterScope(humanId.Value, nameof(DefaultGoalEngine)))
            {
                _log.GoalResolved(humanId.Value.ToString(), goal.Kind.ToString(), goal.Resolution!.Value.ToString(),
                    goal.Progress, goal.Frustration);
            }
        }

        private void LogSeeded(PersistentGoal goal)
        {
            _log.GoalSeededFromPersonality("(seeding)", goal.Kind.ToString(), goal.Salience);
        }

        /// <summary>
        /// Returns (strong, weak) relevance flags for a given goal and committed action.
        /// Both can be false if the action is not relevant to the goal.
        /// </summary>
        private static (bool strong, bool weak) IsGoalRelevantAction(PersistentGoal goal, ActionCommitted ac)
        {
            return goal.Kind switch
            {
                PersistentGoalKind.MasterCraft =>
                    ac.ActionName == Work ? (true, false) :
                    ac.ActionName == Create ? (true, false) :
                    (false, false),

                PersistentGoalKind.BuildReputation =>
                    ac.ActionName == Work ? (false, true) :
                    ac.ActionName == ReachOut ? (false, true) :
                    (false, false),

                PersistentGoalKind.FindMeaning =>
                    ac.ActionName == Create ? (true, false) :
                    ac.ActionName == Work ? (false, true) :
                    ac.ActionName == ReachOut ? (false, true) :
                    (false, false),

                PersistentGoalKind.OvercomeTrauma =>
                    ac.ActionName == SelfCare ? (true, false) :
                    ac.ActionName == ReachOut ? (true, false) :
                    (false, false),

                PersistentGoalKind.BuildIdentity =>
                    ac.ActionName == Create ? (true, false) :
                    ac.ActionName == ReachOut ? (false, true) :
                    (false, false),

                PersistentGoalKind.FindPartner =>
                    ac.ActionName == InviteIntimacy ? (true, false) :
                    ac.ActionName == ReachOut ? (false, true) :
                    (false, false),

                PersistentGoalKind.RepairRelationship =>
                    ac.ActionName == ReachOut && goal.TargetHuman is not null && ac.TargetHuman == goal.TargetHuman
                        ? (true, false)
                        : (false, false),

                PersistentGoalKind.ProtectFamily =>
                    ac.ActionName == ReachOut && goal.TargetHuman is not null && ac.TargetHuman == goal.TargetHuman
                        ? (false, true)
                        : (false, false),

                PersistentGoalKind.EscapeDanger =>
                    ac.ActionName == MoveToPrivate ? (true, false) :
                    ac.ActionName == MoveToRest ? (false, true) :
                    (false, false),

                PersistentGoalKind.SeekRevenge =>
                    ac.ActionName == ReachOut && goal.TargetHuman is not null && ac.TargetHuman == goal.TargetHuman
                        ? (true, false)
                        : (false, false),

                _ => (false, false)
            };
        }

        private static PersistentGoal BuildGoal(PersistentGoalKind kind, GoalOrigin origin, double salience, WDateTime now)
            => new(Guid.NewGuid(), kind, origin, salience, 0.0, 0.0, now, now);

        private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);

        #endregion
    }
}
