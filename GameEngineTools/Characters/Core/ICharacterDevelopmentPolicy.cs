// ICharacterDevelopmentPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using static GameEngineTools.Characters.Engines.ActionNames;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Runtime policy for age/stadium-dependent character affordances.
    /// Keeps development gating out of individual need and modifier engines.
    /// </summary>
    public interface ICharacterDevelopmentPolicy
    {
        /// <summary>Resolves the runtime stadium for a character at the given world time.</summary>
        StadiumType ResolveStadium(IHumanContext context, WDateTime now);

        /// <summary>Returns whether a behavior action is developmentally available.</summary>
        bool AllowsAction(StadiumType stadium, string actionName);

        /// <summary>Returns whether adult intimacy/reproduction behavior can be considered.</summary>
        bool AllowsAdultIntimacy(StadiumType stadium);
    }
}
