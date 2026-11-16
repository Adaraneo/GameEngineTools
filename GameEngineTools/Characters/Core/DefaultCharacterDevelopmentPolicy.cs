// DefaultCharacterDevelopmentPolicy.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using static GameEngineTools.Characters.Engines.ActionNames;
    using GameEngineTools.World.Utils.Time;

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
                throw new InvalidOperationException(
                    $"Cannot resolve life stage for character {context.Id}: HumanContext.Identity is null. " +
                    "Development gating (e.g. blocking Work/Create for children) silently degrades to Adult " +
                    "when Identity is missing, so the context must carry a valid Identity with a BirthDate.");
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
