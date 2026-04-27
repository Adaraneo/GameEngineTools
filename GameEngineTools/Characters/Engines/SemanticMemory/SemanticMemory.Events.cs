// SemanticMemory.Events.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Emitováno kdykoliv se aktualizuje přesvědčení postavy o jiné osobě.
    /// Konzumuje BehaviorEngine, targeting subsystém a logování.
    /// </summary>
    public sealed record SemanticBeliefUpdated(
        WDateTime OccurredAt,
        /// <summary>Postava, jejíž přesvědčení se aktualizovalo.</summary>
        HumanId Human,
        /// <summary>Osoba, o které se přesvědčení aktualizovalo.</summary>
        HumanId Other,
        /// <summary>Druh přesvědčení, které se změnilo.</summary>
        PersonBeliefKind Kind,
        /// <summary>Nová hodnota Strength po aktualizaci [0.0–1.0].</summary>
        double Strength,
        /// <summary>Celkový počet evidencí pro tento belief kind.</summary>
        int EvidenceCount) : IDomainEvent;
}
