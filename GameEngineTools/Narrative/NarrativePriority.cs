// NarrativePriority.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    /// <summary>
    /// Priorita narativního záznamu — určuje, jak důležitá událost je pro hráče.
    /// </summary>
    /// <remarks>
    /// Používáš ji k filtrování: zobrazit jen <see cref="High"/> a <see cref="Medium"/>
    /// v herním UI, <see cref="Low"/> pouze v debug deníku.
    /// </remarks>
    public enum NarrativePriority
    {
        /// <summary>
        /// Každodenní rutina — jídlo, spánek, odpočinek, self-care.
        /// Zobrazuj pouze v podrobném deníkovém módu.
        /// </summary>
        Low,

        /// <summary>
        /// Sociální nebo emocionálně zajímavá událost.
        /// Vhodné pro herní UI (dialog box, plovoucí zpráva, zápis do deníku).
        /// </summary>
        Medium,

        /// <summary>
        /// Zlomový moment — první dojem, odmítnutí intimity, noční můra, smíření.
        /// Zaslouží zvýraznění, animaci, nebo notifikaci.
        /// </summary>
        High
    }
}
