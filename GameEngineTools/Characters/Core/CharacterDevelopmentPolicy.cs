// CharacterDevelopmentPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using GameEngineTools.World.Utils.Time;
    using static GameEngineTools.Characters.Engines.ActionNames;

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

    /// <summary>
    /// Default conservative development policy.
    /// Baby and child stages are intentionally protected from adult social/competence behavior.
    /// </summary>
    public sealed class DefaultCharacterDevelopmentPolicy : ICharacterDevelopmentPolicy
    {
        #region ICharacterDevelopmentPolicy

        /// <inheritdoc/>
        public StadiumType ResolveStadium(IHumanContext context, WDateTime now)
        {
            if (context.Identity is null)
            {
                return StadiumType.Adult;
            }

            var ageYears = AgeYears(context.Identity.BirthDate, now.Date);
            return StadiumResolver.Resolve(Math.Max(0, ageYears));
        }

        /// <inheritdoc/>
        public bool AllowsAction(StadiumType stadium, string actionName)
            => stadium switch
            {
                StadiumType.Baby => actionName is Eat or Drink or SelfCare or Idle or MoveToRest,
                StadiumType.Child => actionName is not (Work or Create or MoveToWork or InviteIntimacy or MoveToPrivate),
                StadiumType.Teenager => actionName is not InviteIntimacy,
                _ => true
            };

        /// <inheritdoc/>
        public bool AllowsAdultIntimacy(StadiumType stadium)
            => stadium is StadiumType.Adult or StadiumType.MidAged or StadiumType.Old;

        #endregion ICharacterDevelopmentPolicy

        #region Helpers

        private static int AgeYears(WDateOnly birth, WDateOnly today)
        {
            var age = today.Year - birth.Year;
            if (today.Month < birth.Month ||
                (today.Month == birth.Month && today.Day < birth.Day))
            {
                age--;
            }

            return age;
        }

        #endregion Helpers
    }
}
