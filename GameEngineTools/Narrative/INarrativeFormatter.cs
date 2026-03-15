// INarrativeFormatter.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using System;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Rozhraní pro formátování doménových událostí na čitelný narativní text.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč interface, ne statická třída?</b><br/>
    /// Testovatelnost — v testech <c>SimulationScene</c> si mockuješ formatter
    /// a ověřuješ, že byl zavolán se správnými argumenty.<br/>
    /// Rozšiřitelnost — v budoucnu přidáš <c>EnglishNarrativeFormatter</c>,
    /// <c>DebugNarrativeFormatter</c> nebo AI-generovaný formatter — beze změny SimulationScene.
    /// </para>
    /// <para>
    /// <b>Návratová hodnota <c>null</c>:</b><br/>
    /// Formatter vrací <c>null</c> pro eventy, které nejsou narativně zajímavé
    /// (např. <c>SleepPhaseChanged</c> — debugovací info, ne příběh).
    /// Volající layer null výstupy ignoruje.
    /// </para>
    /// <para>
    /// <b>Proč <see cref="NarrativeCharacterInfo"/> místo pouhého <c>string</c>?</b><br/>
    /// Viz dokumentaci záznamu — kvůli gramatickému rodu v češtině.
    /// </para>
    /// </remarks>
    public interface INarrativeFormatter
    {
        /// <summary>
        /// Zformátuje doménový event na čitelný narativní záznam.
        /// </summary>
        /// <param name="ev">Doménový event k formátování.</param>
        /// <param name="resolveCharacter">
        /// Funkce pro překlad <see cref="HumanId"/> na informace o postavě.
        /// Formatter ji volá pro každou postavu zmíněnou v eventu.
        /// </param>
        /// <returns>
        /// Narativní záznam, nebo <c>null</c> pokud event není zajímavý pro narativ.
        /// </returns>
        NarrativeEntry? Format(IDomainEvent ev, Func<HumanId, NarrativeCharacterInfo> resolveCharacter);
    }
}
