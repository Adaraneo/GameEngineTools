// BehaviorIntentMapper.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior.Intent
{
    using static ActionNames;

    internal static class BehaviorIntentMapper
    {
        internal static BehaviorIntentKind Resolve(string actionName) => actionName switch
        {
            Work or Create or MoveToWork => BehaviorIntentKind.WorkSession,
            MoveToRest => BehaviorIntentKind.RestSeeking,
            ReachOut or MoveToSocial => BehaviorIntentKind.SocialSeeking,
            MoveToPrivate or InviteIntimacy => BehaviorIntentKind.PrivacySeeking,
            SelfCare => BehaviorIntentKind.SelfCare,
            MoveToPublic => BehaviorIntentKind.Exploration,
            Idle => BehaviorIntentKind.None,
            _ => BehaviorIntentKind.None,
        };

        internal static bool Matches(ActiveIntent intent, string actionName)
            => intent.Kind != BehaviorIntentKind.None && Resolve(actionName) == intent.Kind;
    }
}
