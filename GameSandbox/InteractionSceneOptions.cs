// InteractionSceneOptions.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Sleep;
using GameEngineTools.Characters.GameObjects;
using GameEngineTools.World.Utils.Time;

namespace GameSandbox.Scenes
{
    /// <summary>
    /// Konfigurace pro <see cref="InteractionScene"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč Options objekt místo parametrů konstruktoru?</b><br/>
    /// Kdybychom předávali každou hodnotu jako samostatný parametr konstruktoru,
    /// dostali bychom toto:
    /// <code>
    /// new InteractionScene(runtime, path, player, npc, 2, 0.5, true, myCallback) // ❌ nečitelné
    /// </code>
    /// S Options objektem je každá vlastnost pojmenovaná a kód se čte jako věta:
    /// <code>
    /// new InteractionSceneOptions          // ✅ čitelné
    /// {
    ///     Player          = player,
    ///     Npc             = npc,
    ///     SimulationYears = 2,
    ///     OnTick          = MyScenario
    /// }
    /// </code>
    /// Navíc — přidáš novou volbu? Stačí přidat property. Žádná breaking change v konstruktoru.
    /// </para>
    /// </remarks>
    public sealed class InteractionSceneOptions
    {
        #region Účastníci

        /// <summary>
        /// Hráčská postava účastnící se simulace.
        /// </summary>
        /// <remarks>
        /// Musí být již importována a přidána do manageru před spuštěním scény.
        /// </remarks>
        public required CharacterBase Player { get; init; }

        /// <summary>
        /// NPC s nímž probíhají interakce.
        /// </summary>
        public required CharacterBase Npc { get; init; }

        #endregion

        #region Časování simulace

        /// <summary>
        /// Počet herních let, po které simulace poběží.
        /// Výchozí hodnota: <c>2</c>.
        /// </summary>
        public int SimulationYears { get; init; } = 2;

        /// <summary>
        /// Délka jednoho simulačního kroku — jak daleko se engine posune v každé iteraci smyčky.
        /// Výchozí hodnota: <c>0.5 herní hodiny</c>.
        /// </summary>
        /// <remarks>
        /// Čím kratší krok, tím přesnější simulace — ale tím víc iterací (pomalejší).
        /// Pro sandbox testování je 0.5h dobrý kompromis.
        /// </remarks>
        public WTimeSpan TickStep { get; init; } = WTimeSpan.FromHours(0.5);

        /// <summary>
        /// O kolik herního času se hodiny posunou na konci každé iterace smyčky.
        /// Výchozí hodnota: <c>1 herní hodina</c>.
        /// </summary>
        /// <remarks>
        /// Pozor: <see cref="ClockAdvance"/> by měl být ≥ <see cref="TickStep"/>.
        /// Pokud by byl menší, simuluješ stejný okamžik víckrát.
        /// </remarks>
        public WTimeSpan ClockAdvance { get; init; } = WTimeSpan.FromHours(1);

        #endregion

        #region Scénář (callback)

        /// <summary>
        /// Callback volaný na začátku každého ticku — zde definuješ svůj scénář.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Toto je srdce "obecnosti" scény. Místo natvrdo zakódovaných pravidel
        /// předáš svou vlastní funkci, která rozhodne co se v daném ticku stane.
        /// </para>
        /// <para>
        /// Signatura:
        /// <code>
        /// void OnTick(WDateTime now, IHuman player, IHuman npc)
        /// </code>
        /// </para>
        /// <para>
        /// Příklad použití v Program.cs:
        /// <code>
        /// OnTick = (now, player, npc) =>
        /// {
        ///     if (now.Day is 2 or 6 or 12)
        ///         npc.ReceiveEvent(new InteractionProposed(now, player.Id, npc.Id, SpeechAct.SmallTalk, "Ahoj!"));
        ///
        ///     if (now.Day is 10)
        ///         player.ReceiveEvent(new InteractionProposed(now, npc.Id, player.Id, SpeechAct.Validation, "Sluší ti to."));
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// Pokud <c>null</c>, žádné plánované eventy se neodesílají —
        /// scéna simuluje pouze přirozené chování enginů (ReachOut, spánek, atd.).
        /// </para>
        /// </remarks>
        public Action<WDateTime, IHuman, IHuman>? OnTick { get; init; }

        #endregion

        #region Sleep handling

        /// <summary>
        /// Určuje jak scéna reaguje na sleep prompty hráče.
        /// Výchozí hodnota: <see cref="SleepHandling.Auto"/> — potvrdí automaticky jako NPC.
        /// </summary>
        /// <remarks>
        /// Nastav na <see cref="SleepHandling.Manual"/> až budeš mít UI —
        /// scéna pak zavolá <see cref="OnSleepPrompt"/> a čeká na tvůj vstup.
        /// </remarks>
        public SleepHandling PlayerSleepHandling { get; init; } = SleepHandling.Auto;

        /// <summary>
        /// Callback volaný když hráč dostane sleep prompt a <see cref="PlayerSleepHandling"/>
        /// je <see cref="SleepHandling.Manual"/>.
        /// </summary>
        /// <remarks>
        /// Vrátí <c>true</c> = hráč jde spát, <c>false</c> = odmítá spánek.
        /// <code>
        /// OnSleepPrompt = prompt => {
        ///     Console.WriteLine("Jít spát? (A/N)");
        ///     return Console.ReadKey(true).Key == ConsoleKey.A;
        /// }
        /// </code>
        /// </remarks>
        public Func<SleepPromptRequested, bool>? OnSleepPrompt { get; init; }

        #endregion
    }

    /// <summary>
    /// Způsob zpracování sleep promptů.
    /// </summary>
    public enum SleepHandling
    {
        /// <summary>Spánek se automaticky potvrdí — vhodné pro NPC a sandbox bez UI.</summary>
        Auto,

        /// <summary>Scéna zavolá callback <see cref="InteractionSceneOptions.OnSleepPrompt"/> a čeká na rozhodnutí.</summary>
        Manual
    }
}
