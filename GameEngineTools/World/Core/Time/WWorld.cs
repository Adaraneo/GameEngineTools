// WWorld.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Static ambient context of world time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Configure-once pattern.</b> Set once at game start — after that
    /// all W-types (<see cref="GameEngineTools.World.Utils.Time.WDateTime"/>,
    /// <see cref="GameEngineTools.World.Utils.Time.WDateOnly"/>, etc.) behave
    /// like <see cref="DateTime"/>. No passing of context through parameters.
    /// </para>
    /// <para>
    /// This is the same pattern as <c>TimeZoneInfo.Local</c> or
    /// <c>CultureInfo.CurrentCulture</c> — legitimate for an application where there is
    /// a single world and a single spec that never changes at runtime.
    /// </para>
    /// <para>
    /// Usage example:
    /// <code>
    /// // Once at startup (in GameEngineToolsRuntime):
    /// WWorld.Configure(spec, clock);
    ///
    /// // Then anywhere without passing parameters:
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
        private static IClock? _clock;

        #endregion Privátní stav

        #region Veřejné vlastnosti

        /// <summary>
        /// World-calendar specification valid for the current game.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If <see cref="Configure"/> was not called before first use.
        /// </exception>
        public static WorldTimeSpec Spec
            => _spec ?? throw new InvalidOperationException(
                "WWorld není nakonfigurován. Zavolej WWorld.Configure(spec, clock) před použitím W-typů.");

        /// <summary>
        /// Game clock — the source of the current game time.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// If <see cref="Configure"/> was not called before first use.
        /// </exception>
        public static IClock Clock
            => _clock ?? throw new InvalidOperationException(
                "WWorld není nakonfigurován. Zavolej WWorld.Configure(spec, clock) před použitím W-typů.");

        /// <summary>
        /// Returns <c>true</c> if <see cref="Configure"/> has already been called.
        /// Used for a safe fallback in the <c>ToString()</c> methods of the W-types.
        /// </summary>
        public static bool IsConfigured => _spec != null && _clock != null;

        #endregion Veřejné vlastnosti

        #region Konfigurace

        /// <summary>
        /// Sets the world specification and the game clock.
        /// </summary>
        /// <param name="spec">
        /// World-calendar specification — calendar, day length, ticks per second.
        /// </param>
        /// <param name="clock">
        /// Game clock — returns the current game time from the game loop,
        /// NOT a real-time mapping (<see cref="IWorldClock"/>).
        /// </param>
        /// <remarks>
        /// <para>
        /// Call once at game start — typically in <c>GameEngineToolsRuntime.StartAsync</c>
        /// nebo v <c>TestBase.InitializeServicesAndGetProvider</c>.
        /// </para>
        /// <para>
        /// A second call to <see cref="Configure"/> is allowed (it overwrites previous values).
        /// This normally does not happen in production code — useful for tests.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">
        /// Pokud je <paramref name="spec"/> nebo <paramref name="clock"/> null.
        /// </exception>
        public static void Configure(WorldTimeSpec spec, IClock clock)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(clock);
            _spec = spec;
            _clock = clock;
        }

        #endregion Konfigurace

        #region Testovací utility

        /// <summary>
        /// Resets the ambient state to an unconfigured state.
        /// </summary>
        /// <remarks>
        /// <b>Do not call in production code.</b>
        /// Use it in <c>[TestCleanup]</c> for test isolation — each test should
        /// start with a clean slate and call <see cref="Configure"/> in <c>[TestInitialize]</c>.
        /// </remarks>
        internal static void Reset()
        {
            _spec = null;
            _clock = null;
        }

        #endregion Testovací utility
    }
}
