// SemanticMemory.Types.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.SemanticMemory
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Druh sémantického přesvědčení o osobě.
    /// Každý kind odpovídá jedné dimenzi subjektivního modelu toho, jak se druhý člověk chová.
    /// </summary>
    public enum PersonBeliefKind
    {
        /// <summary>Osoba opakovaně odmítá kontakt nebo interakci. Blokuje sociální přiblížení.</summary>
        Rejecting,

        /// <summary>Osoba přijímá vulnerability bez odsuzování. Podmínka pro SelfDisclosure/Meta/Invite.</summary>
        EmotionallySafe,

        /// <summary>Osoba dodržuje sliby, pomáhá a opravuje škody. Základ důvěry.</summary>
        Reliable,

        /// <summary>Osoba reaguje s teplem a pozitivitou. Zvyšuje baseline pro veškerý kontakt.</summary>
        Warm,

        /// <summary>Osoba kritizuje, zanedbává nebo přehlíží. Potlačuje vulnerability.</summary>
        Critical
    }

    /// <summary>
    /// Přímá evidence pro konkrétní belief kind — dodaná z interakčního nebo vztahového enginu.
    /// Doplňuje pattern inference z epizodické paměti.
    /// </summary>
    public sealed record PersonBeliefEvidence(
        HumanId Other,
        PersonBeliefKind Kind,
        double Weight,
        string Source);

    /// <summary>
    /// Jedno přesvědčení o konkrétní osobě v jedné dimenzi (<see cref="Kind"/>).
    /// Strength roste s evidencí, klesá přirozeným decay a contradikčním tlakem.
    /// Stability zpomaluje decay a zvyšuje resistenci vůči novým signálům.
    /// </summary>
    public sealed record PersonBelief(
        HumanId Other,
        PersonBeliefKind Kind,
        /// <summary>Aktuální síla přesvědčení [0.0–1.0].</summary>
        double Strength,
        /// <summary>Stabilita — jak obtížně se přesvědčení mění [0.0–0.95].</summary>
        double Stability,
        /// <summary>Celkový počet evidencí, které toto přesvědčení podpořily.</summary>
        int EvidenceCount,
        /// <summary>Čas poslední aktualizace — proxy pro last contact s danou osobou.</summary>
        WDateTime LastUpdatedAt,
        /// <summary>Zdroj poslední aktualizace (pro diagnostiku).</summary>
        string? LastEvidenceSource = null);

    /// <summary>
    /// Kolekce všech přesvědčení o jedné konkrétní osobě.
    /// </summary>
    public sealed record PersonBeliefSet(
        HumanId Other,
        IReadOnlyDictionary<PersonBeliefKind, PersonBelief> Beliefs)
    {
        /// <summary>
        /// Vrátí Strength pro daný <paramref name="kind"/>, nebo 0.0 pokud belief neexistuje.
        /// </summary>
        public double StrengthOf(PersonBeliefKind kind)
            => Beliefs.TryGetValue(kind, out var belief) ? belief.Strength : 0.0;
    }
}
