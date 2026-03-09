// SleepPhase.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Sleep
{
    /// <summary>
    /// Fáze spánkového cyklu postavy.
    /// Každá fáze má jiný rizikový profil a jiné narrative příležitosti.
    /// </summary>
    /// <remarks>
    /// Průchod fázemi: <see cref="Falling"/> → <see cref="Light"/> → <see cref="Deep"/> → <see cref="Rem"/> → <see cref="Light"/> → ...
    /// Postava se probouzí vždy z fáze <see cref="Light"/>.
    /// </remarks>
    public enum SleepPhase
    {
        /// <summary>
        /// Usínání — přechod mezi bdělostí a spánkem.
        /// Postava je snadno přerušitelná, sny ani obnova neprobíhají.
        /// </summary>
        Falling,

        /// <summary>
        /// Lehký spánek — povrchní, přerušitelný (hluk, útok).
        /// Obnova energie probíhá pomalu.
        /// </summary>
        Light,

        /// <summary>
        /// Hluboký spánek — těžko přerušitelný, maximální fyzická obnova.
        /// Riziko přepadení je nízké (postava slyší méně), ale probuzení je těžší.
        /// </summary>
        Deep,

        /// <summary>
        /// REM fáze — fáze snů a psychologické obnovy.
        /// Zde se generují narrative eventy (sny, noční můry).
        /// Fyzické riziko přepadení je nejnižší, psychologické riziko (můry) nejvyšší.
        /// </summary>
        Rem,

        /// <summary>
        /// Probouzení — přechod ze spánku do bdělosti.
        /// Postava ještě není plně akceschopná.
        /// </summary>
        Waking
    }
}
