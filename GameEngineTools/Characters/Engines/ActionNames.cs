// ActionNames.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines
{
    public static class ActionNames
    {
        public const string Sleep = "Sleep";
        public const string Eat = "Eat";
        public const string Drink = "Drink";
        public const string ReachOut = "ReachOut";
        public const string Work = "Work";
        public const string Create = "Create";
        public const string SelfCare = "SelfCare";
        public const string InviteIntimacy = "InviteIntimacy";
        public const string Flee = "Flee";
        public const string Fight = "Fight";
        public const string Idle = "Idle";

        public const string MoveToSocial = "MoveTo:Social";
        public const string MoveToPrivate = "MoveTo:Private";
        public const string MoveToWork = "MoveTo:Work";
        public const string MoveToRest = "MoveTo:Rest";
        public const string MoveToPublic = "MoveTo:Public";
        public const string MoveToFood = "MoveTo:Food";
        public const string MoveToDrink = "MoveTo:Drink";

        public const string InteractWithObject = "InteractWithObject";

        #region Affordance-driven object interactions

        /// <summary>Sit or rest at a bench, chair, or similar furniture. Slot: Posture only.</summary>
        public const string UseObjectForRest = "UseObject:Rest";

        /// <summary>Work at a workbench, desk, forge, or tool. Slots: Hands + Mind.</summary>
        public const string UseObjectForWork = "UseObject:Work";

        /// <summary>Play a lute, game board, or entertainment object. Slots: Hands + Mind.</summary>
        public const string UseObjectForFun = "UseObject:Fun";

        /// <summary>Stand near a fireplace, brazier, or warm spring. Slot: None (passive).</summary>
        public const string UseObjectForWarmth = "UseObject:Warmth";

        /// <summary>Observe a painting, garden, or pleasant ambient object. Slot: None (passive).</summary>
        public const string UseObjectForMood = "UseObject:Mood";

        /// <summary>Gather near a communal fireplace, fountain, or social anchor. Slot: None.</summary>
        public const string GatherAtObject = "UseObject:Social";

        #endregion Affordance-driven object interactions
    }
}
