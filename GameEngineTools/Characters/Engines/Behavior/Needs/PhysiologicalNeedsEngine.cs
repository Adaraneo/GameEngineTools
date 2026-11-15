// PhysiologicalNeedsEngine.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Needs
{
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Utils.Time;
    using static ActionNames;

    /// <summary>
    /// Produces baseline body-regulation candidates such as food, water, self-care, and rest movement.
    /// </summary>
    internal sealed class PhysiologicalNeedsEngine : IBehaviorNeedEngine
    {
        #region IBehaviorNeedEngine

        public BehaviorNeedOutput Evaluate(BehaviorContext context)
        {
            var ph = context.HumanContext.Snapshot.Physiology;
            var needSelfCare = BehaviorMath.ComputeSelfCareNeed(ph);
            var cfg = context.Config;

            // The best drink in reach sets the rate (its own hydration value, else the default); a meal runs until hunger reaches its target.
            var drinkRate = context.AvailableObjects?
                .Where(o => o.Category == GameEngineTools.World.Objects.WorldObjectCategory.Drink && o.IsAvailable && o.NutritionalProfile?.HydrationGain is > 0.0)
                .Select(o => o.NutritionalProfile!.HydrationGain!.Value)
                .DefaultIfEmpty(cfg.DrinkRatePerHour).Max() ?? cfg.DrinkRatePerHour;
            var drinkDuration = BehaviorMath.ConsumptionDuration(ph.Thirst, cfg.DrinkTargetThirst, drinkRate, cfg.DrinkMinMinutes, cfg.DrinkMaxMinutes);
            var eatDuration = BehaviorMath.ConsumptionDuration(ph.Hunger, cfg.EatTargetHunger, cfg.EatRatePerHour, cfg.EatMinMinutes, cfg.EatMaxMinutes);

            return new BehaviorNeedOutput(
                new[]
                {
                    new BehaviorDrive(nameof(context.State.NeedFood), context.State.NeedFood, BehaviorDomain.Physiological),
                    new BehaviorDrive(nameof(context.State.NeedWater), context.State.NeedWater, BehaviorDomain.Physiological),
                    new BehaviorDrive(nameof(context.State.NeedRest), context.State.NeedRest, BehaviorDomain.Physiological),
                    new BehaviorDrive("NeedSelfCare", needSelfCare, BehaviorDomain.Physiological)
                },
                new List<BehaviorCandidate>
                {
                    new BehaviorCandidate(Eat, BehaviorMath.Util(context.State.NeedFood, 1.2), eatDuration, BehaviorDomain.Physiological),
                    new BehaviorCandidate(Drink, BehaviorMath.Util(context.State.NeedWater, 1.1), drinkDuration, BehaviorDomain.Physiological),
                    new BehaviorCandidate(SelfCare, BehaviorMath.Util(needSelfCare, 0.5), WTimeSpan.FromHours(0.5), BehaviorDomain.Physiological, new[] { "PrivateSurface" }),
                    new BehaviorCandidate(Idle, BehaviorMath.Util(10, 0.3), WTimeSpan.FromMinutes(30), BehaviorDomain.Physiological),
                    new BehaviorCandidate(MoveToRest, context.State.NeedRest, WTimeSpan.FromMinutes(20), BehaviorDomain.Physiological, new[] { "EnvironmentMovement" })
                });
        }

        #endregion IBehaviorNeedEngine
    }
}
