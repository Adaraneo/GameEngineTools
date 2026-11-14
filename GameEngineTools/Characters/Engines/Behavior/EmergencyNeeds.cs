// EmergencyNeeds.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using static ActionNames;

    /// <summary>Which of hunger and thirst to answer first when both are critical: the one with the least time left to the lethal level.</summary>
    internal static class EmergencyNeeds
    {
        internal enum Need { None, Hunger, Thirst }

        /// <summary>The more urgent need when BOTH are at the emergency level, otherwise <see cref="Need.None"/>; a tie goes to thirst.</summary>
        internal static Need MostUrgent(BehaviorContext context)
        {
            var cfg = context.Config;
            var food = context.State.NeedFood;
            var water = context.State.NeedWater;
            if (food < cfg.EmergencyIntentOverrideThreshold || water < cfg.EmergencyIntentOverrideThreshold)
                return Need.None;

            var hoursToStarve = (cfg.LethalNeedLevel - food) / cfg.HungerRisePerHour;
            var hoursToDehydrate = (cfg.LethalNeedLevel - water) / cfg.ThirstRisePerHour;
            return hoursToDehydrate <= hoursToStarve ? Need.Thirst : Need.Hunger;
        }

        internal static Need Other(Need need) => need switch { Need.Hunger => Need.Thirst, Need.Thirst => Need.Hunger, _ => Need.None };

        internal static bool Answers(Need need, string action) => need switch
        {
            Need.Hunger => action is Eat or EatStored or MoveToFood,
            Need.Thirst => action is Drink or MoveToDrink,
            _ => false,
        };
    }
}
