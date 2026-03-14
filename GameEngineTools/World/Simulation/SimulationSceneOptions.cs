// SimulationSceneOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Sleep;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Konfigurace pro <see cref="SimulationScene"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Proč Options objekt místo konstruktorových parametrů?</b><br/>
    /// S více postavami by konstruktor byl nepřehledný. Options objekt
    /// dává každé hodnotě jméno a umožňuje přidávat nové vlastnosti
    /// bez breaking change.
    /// </para>
    /// <para>
    /// <b>Zásadní rozdíl oproti starému <c>InteractionSceneOptions</c>:</b><br/>
    /// Místo dvojice <c>Player / Npc</c> předáváš obecný seznam <see cref="Characters"/>.
    /// Scéna neví nic o tom, kdo je hráč a kdo NPC — to je záležitost
    /// volající vrstvy (Program.cs, Unity GameManager, …).
    /// </para>
    /// <para>
    /// <b>Routing ReachOut je záměrně na tobě</b> — scéna ho neřeší automaticky,
    /// protože výběr targetu závisí na herní logice (lokace, záměr…).
    /// Detekcí ReachOut v <see cref="OnTick"/> vidíš <c>LastOutbox</c>
    /// z předchozího ticku a můžeš rozhodnout sám.
    /// </para>
    /// </remarks>
    public sealed class SimulationSceneOptions
    {
        #region Účastníci

        /// <summary>
        /// Všechny postavy účastnící se simulace.
        /// </summary>
        /// <remarks>
        /// Pořadí určuje pořadí tickování — první v seznamu tickuje první.
        /// Pokud máš hráče, konvencí je dát ho na index 0.
        /// </remarks>
        public IReadOnlyList<IHuman> Characters { get; init; } = Array.Empty<IHuman>();

        #endregion Účastníci

        #region Časování simulace

        /// <summary>
        /// Počet herních let, po které simulace poběží.
        /// Výchozí hodnota: <c>2</c>.
        /// </summary>
        public int SimulationYears { get; init; } = 2;

        /// <summary>
        /// Délka jednoho simulačního kroku — jak daleko enginy posunout logiku v každé iteraci.
        /// Výchozí hodnota: <c>0.5 herní hodiny</c>.
        /// </summary>
        /// <remarks>
        /// Čím kratší krok, tím přesnější simulace — ale tím víc iterací a pomalejší běh.
        /// 0.5h je dobrý kompromis pro sandbox.
        /// </remarks>
        public WTimeSpan TickStep { get; init; } = WTimeSpan.FromHours(0.5);

        /// <summary>
        /// O kolik herního času se hodiny posunou na konci každé iterace smyčky.
        /// Výchozí hodnota: <c>1 herní hodina</c>.
        /// </summary>
        /// <remarks>
        /// <b>Invariant:</b> <c>ClockAdvance</c> musí být ≥ <c>TickStep</c>.
        /// Pokud by byl menší, simuluješ stejný okamžik víckrát za sebou.
        /// </remarks>
        public WTimeSpan ClockAdvance { get; init; } = WTimeSpan.FromHours(1);

        #endregion Časování simulace

        #region Scénář (callback)

        /// <summary>
        /// Callback volaný na <b>začátku každého ticku</b>, ještě před <c>Tick()</c> postav.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Dvě role v jednom callbacku:
        /// <list type="number">
        ///   <item>
        ///     <b>Scénář</b> — injekce naplánovaných událostí (den 2 → SmallTalk,
        ///     den 16 → přesun do hradu…). Viz příklad níže.
        ///   </item>
        ///   <item>
        ///     <b>ReachOut routing</b> — v tomto okamžiku je <c>LastOutbox</c>
        ///     každé postavy ještě z <em>předchozího</em> ticku, takže vidíš,
        ///     kdo chce oslovit, a můžeš sám vybrat target.
        ///   </item>
        /// </list>
        /// </para>
        /// <para>
        /// Signatura: <c>void OnTick(WDateTime now, IReadOnlyList&lt;IHuman&gt; characters)</c>
        /// </para>
        /// <example>
        /// <code>
        /// OnTick = (now, chars) =>
        /// {
        ///     var player = chars[0];
        ///     var npc    = chars[1];
        ///
        ///     // Naplánovaný scénář
        ///     if (now.Day is 2 or 6 or 12)
        ///         npc.ReceiveEvent(new InteractionProposed(now, player.Id, npc.Id, SpeechAct.SmallTalk, "Ahoj!"));
        ///
        ///     // ReachOut routing — kdo chce oslovit?
        ///     foreach (var c in chars)
        ///     {
        ///         var reachOut = c.LastOutbox.OfType&lt;ActionCommitted&gt;()
        ///             .FirstOrDefault(a => a.ActionName == "ReachOut");
        ///         if (reachOut == null) continue;
        ///
        ///         // Vyber target — třeba nejbližšího ve stejné lokaci
        ///         var target = chars
        ///             .Where(x => x.Id != c.Id
        ///                 &amp;&amp; x.Snapshot.InteractionSurface.Location == c.Snapshot.InteractionSurface.Location)
        ///             .FirstOrDefault();
        ///
        ///         target?.ReceiveEvent(new InteractionProposed(now, c.Id, target.Id, SpeechAct.SmallTalk, null));
        ///     }
        /// }
        /// </code>
        /// </example>
        /// <para>Pokud <c>null</c>, scéna simuluje pouze přirozené chování enginů.</para>
        /// </remarks>
        public Action<WDateTime, IReadOnlyList<IHuman>>? OnTick { get; init; }

        #endregion Scénář (callback)

        #region Sleep handling

        /// <summary>
        /// Handlery sleep promptů pro jednotlivé postavy.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Klíč: <see cref="HumanId"/> postavy.<br/>
        /// Hodnota: callback <c>Func&lt;SleepPromptRequested, bool&gt;</c> —
        /// vrátí <c>true</c> = jít spát, <c>false</c> = odmítnout.
        /// </para>
        /// <para>
        /// <b>Default chování:</b> pokud postava v tomto slovníku není,
        /// scéna automaticky potvrdí spánek (NPC chování).
        /// </para>
        /// <para>
        /// Příklad — hráč s manuálním promptem:
        /// <code>
        /// SleepPromptHandlers = new Dictionary&lt;HumanId, Func&lt;SleepPromptRequested, bool&gt;&gt;
        /// {
        ///     [player.Id] = _ =>
        ///     {
        ///         Console.WriteLine("[SPÁNEK] Jít spát? (A/n)");
        ///         return Console.ReadKey(true).Key != ConsoleKey.N;
        ///     }
        /// }
        /// </code>
        /// </para>
        /// </remarks>
        public IReadOnlyDictionary<HumanId, Func<SleepPromptRequested, bool>>? SleepPromptHandlers { get; init; }

        #endregion Sleep handling
    }
}
