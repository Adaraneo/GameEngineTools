// WWorld.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Statický ambient kontext světového času.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Configure-once pattern.</b> Nastavíš jednou při startu hry — pak se
    /// všechny W-typy (<see cref="GameEngineTools.World.Utils.Time.WDateTime"/>,
    /// <see cref="GameEngineTools.World.Utils.Time.WDateOnly"/>, atd.) chovají
    /// jako <see cref="DateTime"/>. Žádné předávání contextu přes parametry.
    /// </para>
    /// <para>
    /// Je to stejný vzor jako <c>TimeZoneInfo.Local</c> nebo
    /// <c>CultureInfo.CurrentCulture</c> — legitimní pro aplikaci kde je
    /// jeden svět a jedna spec, která se nikdy nezmění za běhu.
    /// </para>
    /// <para>
    /// Příklad použití:
    /// <code>
    /// // Jednou při startu (v GameEngineToolsRuntime):
    /// WWorld.Configure(spec, clock);
    ///
    /// // Pak kdekoliv bez předávání parametrů:
    /// var now  = WDateTime.Now;
    /// var dt   = WDateTime.New(1324, 1, 1, hour: 6);
    /// int year = dt.Year;
    /// var next = dt.AddMonths(3);
    /// bool past = dt &lt; WDateTime.Now;
    /// string s  = dt.ToString();   // "1324-01-01T06:00:00"
    /// </code>
    /// </para>
    /// </remarks>
    public static class WWorld
    {
        #region Privátní stav

        private static WorldTimeSpec? _spec;
        private static IClock?        _clock;

        #endregion

        #region Veřejné vlastnosti

        /// <summary>
        /// Specifikace světového kalendáře platná pro aktuální hru.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Pokud <see cref="Configure"/> nebyl zavolán před prvním použitím.
        /// </exception>
        public static WorldTimeSpec Spec
            => _spec ?? throw new InvalidOperationException(
                "WWorld není nakonfigurován. Zavolej WWorld.Configure(spec, clock) před použitím W-typů.");

        /// <summary>
        /// Herní hodiny — zdroj aktuálního herního času.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Pokud <see cref="Configure"/> nebyl zavolán před prvním použitím.
        /// </exception>
        public static IClock Clock
            => _clock ?? throw new InvalidOperationException(
                "WWorld není nakonfigurován. Zavolej WWorld.Configure(spec, clock) před použitím W-typů.");

        /// <summary>
        /// Vrátí <c>true</c> pokud byl <see cref="Configure"/> již zavolán.
        /// Používá se pro bezpečný fallback v <c>ToString()</c> metod W-typů.
        /// </summary>
        public static bool IsConfigured => _spec != null && _clock != null;

        #endregion

        #region Konfigurace

        /// <summary>
        /// Nastaví světovou specifikaci a herní hodiny.
        /// </summary>
        /// <param name="spec">
        /// Specifikace světového kalendáře — kalendář, délka dne, ticky za sekundu.
        /// </param>
        /// <param name="clock">
        /// Herní hodiny — vrací aktuální herní čas z game-loop,
        /// NE real-time mapování (<see cref="IWorldClock"/>).
        /// </param>
        /// <remarks>
        /// <para>
        /// Volej jednou při startu hry — typicky v <c>GameEngineToolsRuntime.StartAsync</c>
        /// nebo v <c>TestBase.InitializeServicesAndGetProvider</c>.
        /// </para>
        /// <para>
        /// Druhé volání <see cref="Configure"/> je povoleno (přepíše předchozí hodnoty).
        /// V produkčním kódu to normálně nenastane — useful pro testy.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Pokud je <paramref name="spec"/> nebo <paramref name="clock"/> null.
        /// </exception>
        public static void Configure(WorldTimeSpec spec, IClock clock)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(clock);
            _spec  = spec;
            _clock = clock;
        }

        #endregion

        #region Testovací utility

        /// <summary>
        /// Resetuje ambient stav na nenakonfigurovaný stav.
        /// </summary>
        /// <remarks>
        /// <b>Nevolej v produkčním kódu.</b>
        /// Používej v <c>[TestCleanup]</c> pro izolaci testů — každý test by měl
        /// začínat čistým slate a volat <see cref="Configure"/> v <c>[TestInitialize]</c>.
        /// </remarks>
        internal static void Reset()
        {
            _spec  = null;
            _clock = null;
        }

        #endregion
    }
}
