// WorldClock.cs
// Copyright (c) 50PSoftware

using System.Runtime.InteropServices;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Lineární mapování reálného času (Earth UNIX ticks = 100 ns od 1970-01-01Z)
    /// na světový čas (worldTicks podle <see cref="WorldTimeSpec"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Registruj jako <c>IWorldClock</c> singleton přes DI.</b>
    /// <c>WorldTimeSpec</c> dostane ze stejného DI kontejneru jako <see cref="WorldTimeContext"/>
    /// — obě sdílí jednu instanci.
    /// </para>
    /// <para>
    /// Příklad registrace:
    /// <code>
    /// services.AddSingleton&lt;WorldTimeSpec&gt;(...);          // nejdřív spec
    /// services.AddSingleton&lt;IWorldClock&gt;(sp =>
    /// {
    ///     var spec      = sp.GetRequiredService&lt;WorldTimeSpec&gt;();
    ///     var beginning = sp.GetRequiredService&lt;WorldBeginning&gt;();
    ///     return WorldClock.AlignNow(spec, beginning.WorldEpochTicks);
    /// });
    /// services.AddSingleton&lt;WorldTimeContext&gt;();            // dostane spec + IWorldClock
    /// </code>
    /// </para>
    /// </remarks>
    public sealed class WorldClock : IWorldClock
    {
        #region OS interop (Windows / POSIX)

        private const long WindowsEpochFileTimeTicks = 116_444_736_000_000_000L;

        [DllImport("libc", EntryPoint = "clock_gettime")]
        private static extern int clock_gettime(int clk_id, out timespec ts);

        [DllImport("kernel32.dll")]
        private static extern void GetSystemTimeAsFileTime(out FILETIME ft);

        [DllImport("kernel32.dll", EntryPoint = "GetSystemTimePreciseAsFileTime")]
        private static extern void GetSystemTimePreciseAsFileTime(out FILETIME ft);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        { public uint dwLowDateTime; public uint dwHighDateTime; }

        /// <remarks>CLOCK_REALTIME = 0</remarks>
        [StructLayout(LayoutKind.Sequential)]
        private struct timespec
        { public long tv_sec; public long tv_nsec; }

        #endregion OS interop (Windows / POSIX)

        #region Soukromá pole

        /// <summary>
        /// Interní reference na spec — používá se pouze pro <see cref="TicksPerSecond"/>.
        /// Není vystavena veřejně: spec je DI singleton dostupný přímo přes
        /// <see cref="WorldTimeContext.Spec"/>.
        /// </summary>
        private readonly WorldTimeSpec _spec;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje hodiny se zadaným zakotvením v reálném čase.
        /// </summary>
        /// <param name="spec">
        /// Specifikace světového času. Musí být stejná instance jako ta registrovaná
        /// do DI a použitá v <see cref="WorldTimeContext"/>.
        /// </param>
        /// <param name="earthEpochUnixTicks">
        /// Reálný čas kotvy ve 100 ns UNIX tickách (počítáno od 1970-01-01Z).
        /// </param>
        /// <param name="worldEpochTicks">
        /// Světové ticky odpovídající okamžiku <paramref name="earthEpochUnixTicks"/>.
        /// Určuje "nultý bod" mapování Earth → World.
        /// </param>
        /// <param name="timeScale">
        /// Rychlost světového času vůči reálnému.
        /// <c>1.0</c> = real-time, <c>2.0</c> = dvojnásobná rychlost.
        /// </param>
        public WorldClock(WorldTimeSpec spec, long earthEpochUnixTicks, long worldEpochTicks, double timeScale = 1.0)
        {
            _spec = spec;
            EarthEpochUnixTicks = earthEpochUnixTicks;
            WorldEpochTicks = worldEpochTicks;
            TimeScale = timeScale;
        }

        #endregion Konstrukce

        #region Vlastnosti (IWorldClock)

        /// <inheritdoc/>
        public double TimeScale { get; }

        #endregion Vlastnosti (IWorldClock)

        #region Vlastnosti (veřejné)

        /// <summary>
        /// Reálný čas kotvy ve 100 ns UNIX tickách.
        /// Referencí pro mapování Earth ↔ World.
        /// </summary>
        public long EarthEpochUnixTicks { get; }

        /// <summary>
        /// Světové ticky odpovídající <see cref="EarthEpochUnixTicks"/>.
        /// </summary>
        public long WorldEpochTicks { get; }

        #endregion Vlastnosti (veřejné)

        #region Factory metody (statické)

        /// <summary>
        /// Vytvoří hodiny zakotvené tak, aby v reálném čase <paramref name="earthAnchorUnixTicks"/>
        /// byl světový čas roven <paramref name="worldEpochTicks"/>.
        /// </summary>
        /// <param name="spec">Specifikace světového času.</param>
        /// <param name="earthAnchorUnixTicks">Reálný kotevní čas (100 ns UNIX ticky).</param>
        /// <param name="worldEpochTicks">Světové ticky odpovídající kotvě.</param>
        /// <param name="timeScale">Rychlost světového času (výchozí 1.0 = real-time).</param>
        public static WorldClock AlignAt(WorldTimeSpec spec, long earthAnchorUnixTicks, long worldEpochTicks, double timeScale = 1.0)
            => new(spec, earthAnchorUnixTicks, worldEpochTicks, timeScale);

        /// <summary>
        /// Vytvoří hodiny zakotvené na aktuální reálný čas tak, aby "teď" ve světě
        /// odpovídalo <paramref name="worldEpochTicks"/>.
        /// </summary>
        /// <param name="spec">Specifikace světového času.</param>
        /// <param name="worldEpochTicks">Světové ticky představující "teď".</param>
        /// <param name="timeScale">Rychlost světového času (výchozí 1.0 = real-time).</param>
        /// <remarks>
        /// Typické použití při startu hry:
        /// <code>
        /// var clock = WorldClock.AlignNow(spec, beginningWorldTicks, timescale: 1.0);
        /// </code>
        /// </remarks>
        public static WorldClock AlignNow(WorldTimeSpec spec, long worldEpochTicks, double timeScale = 1.0)
            => new(spec, SystemUnixTicks(), worldEpochTicks, timeScale);

        #endregion Factory metody (statické)

        #region IWorldClock — aktuální čas

        /// <inheritdoc/>
        public long NowWorldTicks()
            => EarthToWorldTicks(SystemUnixTicks());

        #endregion IWorldClock — aktuální čas

        #region Konverze Earth ↔ World

        /// <summary>
        /// Převede reálný čas (100 ns UNIX ticky) na světové ticky.
        /// </summary>
        /// <param name="earthUnixTicks">Reálný čas v 100 ns UNIX tickách.</param>
        /// <returns>Odpovídající světové ticky.</returns>
        public long EarthToWorldTicks(long earthUnixTicks)
        {
            // Delta reálného času v 100 ns → převod na sekundy → škálování → světové ticky
            long deltaUnix = earthUnixTicks - EarthEpochUnixTicks;
            double deltaWorldSeconds = (deltaUnix / 10_000_000.0) * TimeScale;
            return WorldEpochTicks + (long)(deltaWorldSeconds * _spec.TicksPerSecond);
        }

        /// <summary>
        /// Převede světové ticky zpět na reálný čas (100 ns UNIX ticky).
        /// </summary>
        /// <param name="worldTicks">Světové ticky k převodu.</param>
        /// <returns>Odpovídající reálný čas v 100 ns UNIX tickách.</returns>
        public long WorldToEarthUnixTicks(long worldTicks)
        {
            long deltaWorldTicks = worldTicks - WorldEpochTicks;
            double deltaWorldSeconds = deltaWorldTicks / (double)_spec.TicksPerSecond;
            long deltaUnix = (long)(deltaWorldSeconds / TimeScale * 10_000_000.0);
            return EarthEpochUnixTicks + deltaUnix;
        }

        #endregion Konverze Earth ↔ World

        #region Systémový čas (statický)

        /// <summary>
        /// Vrátí aktuální systémový čas jako 100 ns UNIX ticky (od 1970-01-01Z).
        /// Funguje na Windows i Linuxu/macOS (POSIX).
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">
        /// Pokud platforma nepodporuje ani jeden ze způsobů čtení systémového času.
        /// </exception>
        public static long SystemUnixTicks()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    GetSystemTimePreciseAsFileTime(out var ft);
                    return FileTimeToUnixTicks(ft);
                }
                catch
                {
                    GetSystemTimeAsFileTime(out var ft);
                    return FileTimeToUnixTicks(ft);
                }
            }

            if (clock_gettime(0, out var ts) == 0)
            {
                return ts.tv_sec * 10_000_000L + (ts.tv_nsec / 100);
            }

            throw new PlatformNotSupportedException("Nepodařilo se získat systémový čas.");
        }

        #endregion Systémový čas (statický)

        #region Privátní pomocné metody

        /// <summary>
        /// Převede Windows FILETIME (100 ns od 1601-01-01) na UNIX ticky (100 ns od 1970-01-01).
        /// </summary>
        private static long FileTimeToUnixTicks(FILETIME ft)
            => (((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime) - WindowsEpochFileTimeTicks;

        #endregion Privátní pomocné metody
    }
}
