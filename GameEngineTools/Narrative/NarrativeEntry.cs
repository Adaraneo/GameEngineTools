// NarrativeEntry.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Jeden narativní záznam — výsledek překladu doménového eventu do čitelné věty.
    /// </summary>
    /// <param name="OccurredAt">Herní čas události.</param>
    /// <param name="Subject">
    /// Hlavní postava věty — "hrdina" záznamu.
    /// Používá se pro filtrování deníku konkrétní postavy.
    /// </param>
    /// <param name="Text">Čitelná věta popisující událost (v češtině).</param>
    /// <param name="Priority">Důležitost záznamu pro hráče.</param>
    public sealed record NarrativeEntry(
        WDateTime OccurredAt,
        HumanId Subject,
        string Text,
        NarrativePriority Priority);
}
