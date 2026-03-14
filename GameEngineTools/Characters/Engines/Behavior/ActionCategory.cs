// ActionCategory.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Behavior
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    internal enum ActionCategory
    {
        /// <summary>
        /// Soustředěná tvorba - Work, Create
        /// </summary>
        Productive,
        /// <summary>
        /// Sociální interakce - ReachOut, InviteIntimacy
        /// </summary>
        Social,
        /// <summary>
        /// Tělesné potřeby - Eat, Drink, SelfCare
        /// </summary>
        Biological,
        /// <summary>
        /// Pasivní odpočinek - Idle
        /// </summary>
        Rest
    }
}
