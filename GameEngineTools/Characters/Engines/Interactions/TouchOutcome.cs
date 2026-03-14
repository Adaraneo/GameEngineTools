// TouchOutcome.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Interactions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Výsledek pokusu o fyzický kontakt.
    /// </summary>
    public sealed record TouchOutcome(
        WDateTime OccurredAt,
        HumanId From,
        HumanId To,
        TouchLevel Level,
        bool Accepted,
        string Reason) : IDomainEvent;
}
