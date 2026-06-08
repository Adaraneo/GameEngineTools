// ActionNames.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines
{
    /// <summary>Canonical string identifiers for every action the behavior engine can select.</summary>
    public static class ActionNames
    {
        /// <summary>Sleep (handled as a session outside utility arbitration).</summary>
        public const string Sleep = "Sleep";
        /// <summary>Eat to reduce hunger.</summary>
        public const string Eat = "Eat";
        /// <summary>Drink to reduce thirst.</summary>
        public const string Drink = "Drink";
        /// <summary>Reach out socially to another character.</summary>
        public const string ReachOut = "ReachOut";
        /// <summary>Perform productive work.</summary>
        public const string Work = "Work";
        /// <summary>Engage in creative activity.</summary>
        public const string Create = "Create";
        /// <summary>Self-care / recovery activity.</summary>
        public const string SelfCare = "SelfCare";
        /// <summary>Invite intimacy with another character.</summary>
        public const string InviteIntimacy = "InviteIntimacy";
        /// <summary>Flee from a threat.</summary>
        public const string Flee = "Flee";
        /// <summary>Fight a threat (approach-motivated).</summary>
        public const string Fight = "Fight";
        /// <summary>Do nothing in particular.</summary>
        public const string Idle = "Idle";

        /// <summary>Move toward a social location.</summary>
        public const string MoveToSocial = "MoveTo:Social";
        /// <summary>Move toward a private location.</summary>
        public const string MoveToPrivate = "MoveTo:Private";
        /// <summary>Move toward a work location.</summary>
        public const string MoveToWork = "MoveTo:Work";
        /// <summary>Move toward a resting location.</summary>
        public const string MoveToRest = "MoveTo:Rest";
        /// <summary>Move toward a public location.</summary>
        public const string MoveToPublic = "MoveTo:Public";
        /// <summary>Forage: move toward a location with food.</summary>
        public const string MoveToFood = "MoveTo:Food";
        /// <summary>Forage: move toward a location with drink.</summary>
        public const string MoveToDrink = "MoveTo:Drink";

        /// <summary>Interact with a nearby world object.</summary>
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
