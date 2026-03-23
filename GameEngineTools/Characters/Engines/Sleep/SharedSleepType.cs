// SharedSleepType.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Typ sdíleného spánku — určuje kontext, ve kterém postava spí s někým dalším.
    /// Ovlivňuje rizikový profil (hlídání) i narrative příležitosti (rozhovor, sdílený sen).
    /// </summary>
    public enum SharedSleepType
    {
        /// <summary>
        /// Tábor pod širým nebem — společník hlídá na střídačku.
        /// Snižuje riziko přepadení (<see cref="SleepConfig.CompanionGuardModifier"/>).
        /// </summary>
        Camp,

        /// <summary>
        /// Sdílená postel nebo místnost — intimní kontext.
        /// Umožňuje relationship narrative eventy (noční rozhovor, sdílený sen).
        /// Hlídací bonus je nižší než u tábora.
        /// </summary>
        Bed,

        /// <summary>
        /// Nouzový úkryt — spánek ve stresu (skrývání, útěk, nebezpečí).
        /// Zvyšuje stres, zkracuje deep fázi, žádný hlídací bonus.
        /// </summary>
        Emergency,

        Romantic,
        Protective
    }
}
