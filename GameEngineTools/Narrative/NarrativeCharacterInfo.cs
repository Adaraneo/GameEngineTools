// NarrativeCharacterInfo.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// Základní informace o postavě pro formátování narativu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč nepoužíváme jen <c>string</c> jméno?</b><br/>
    /// Čeština vyžaduje gramatický rod pro správné skloňování sloves —
    /// "šel" vs. "šla", "přijal" vs. "přijala". Bez pohlaví bychom museli
    /// buď psát ošklivé "šel/a", nebo ignorovat gramatiku.
    /// </para>
    /// </remarks>
    /// <param name="Name">Jméno postavy zobrazené v narativu (např. "Anna", "Petr").</param>
    /// <param name="Biology">Biologické pohlaví — pro správné skloňování v češtině.</param>
    public sealed record NarrativeCharacterInfo(string Name, SexBiology Biology)
    {
        /// <summary>
        /// Vrátí <c>true</c> pokud je postava ženského pohlaví.
        /// Používá se interně pro volbu gramatického rodu ve větách.
        /// </summary>
        public bool IsFemale => Biology == SexBiology.Female;
    }
}
