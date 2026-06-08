// WorldClock.cs
// Copyright (c) 50PSoftware

using System.Runtime.InteropServices;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Linear mapping of real time (Earth UNIX ticks = 100 ns since 1970-01-01Z)
    /// to world time (world ticks per <see cref="WorldTimeSpec"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Register as an <c>IWorldClock</c> singleton via DI.</b>
    /// It receives the <c>WorldTimeSpec</c> from the same DI container as <c>WorldTimeContext</c>
    /// — both share a single instance.
    /// </para>
    /// <para>
    /// Registration example:
    /// <code>
    /// services.AddSingleton&lt;WorldTimeSpec&gt;(...);          // the spec first
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
        /// Internal reference to the spec — used only for <c>TicksPerSecond</c>.
        /// Not exposed publicly: the spec is a DI singleton available directly via
        /// <c>WorldTimeContext.Spec</c>.
        /// </summary>
        private readonly WorldTimeSpec _spec;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Initializes the clock with the given real-time anchor.
        /// </summary>
        /// <param name="spec">
        /// World-time specification. Must be the same instance as the one registered
        /// in DI and used in <c>WorldTimeContext</c>.
        /// </param>
        /// <param name="earthEpochUnixTicks">
        /// Real anchor time in 100 ns UNIX ticks (counted from 1970-01-01Z).
        /// </param>
        /// <param name="worldEpochTicks">
        /// World ticks corresponding to the instant <paramref name="earthEpochUnixTicks"/>.
        /// Defines the "zero point" of the Earth → World mapping.
        /// </param>
        /// <param name="timeScale">
        /// Speed of world time relative to real time.
        /// <c>1.0</c> = real-time, <c>2.0</c> = double speed.
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
        /// Real anchor time in 100 ns UNIX ticks.
        /// Reference point for the Earth ↔ World mapping.
        /// </summary>
        public long EarthEpochUnixTicks { get; }

        /// <summary>
        /// World ticks corresponding to <see cref="EarthEpochUnixTicks"/>.
        /// </summary>
        public long WorldEpochTicks { get; }

        #endregion Vlastnosti (veřejné)

        #region Factory metody (statické)

        /// <summary>
        /// Creates a clock anchored so that at real time <paramref name="earthAnchorUnixTicks"/>
        /// the world time equals <paramref name="worldEpochTicks"/>.
        /// </summary>
        /// <param name="spec">World-time specification.</param>
        /// <param name="earthAnchorUnixTicks">Real anchor time (100 ns UNIX ticks).</param>
        /// <param name="worldEpochTicks">World ticks corresponding to the anchor.</param>
        /// <param name="timeScale">Speed of world time (default 1.0 = real-time).</param>
        public static WorldClock AlignAt(WorldTimeSpec spec, long earthAnchorUnixTicks, long worldEpochTicks, double timeScale = 1.0)
            => new(spec, earthAnchorUnixTicks, worldEpochTicks, timeScale);

        /// <summary>
        /// Creates a clock anchored to the current real time so that "now" in the world
        /// corresponds to <paramref name="worldEpochTicks"/>.
        /// </summary>
        /// <param name="spec">World-time specification.</param>
        /// <param name="worldEpochTicks">World ticks representing "now".</param>
        /// <param name="timeScale">Speed of world time (default 1.0 = real-time).</param>
        /// <remarks>
        /// Typical use at game start:
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
        /// Converts real time (100 ns UNIX ticks) into world ticks.
        /// </summary>
        /// <param name="earthUnixTicks">Real time in 100 ns UNIX ticks.</param>
        /// <returns>The corresponding world ticks.</returns>
        public long EarthToWorldTicks(long earthUnixTicks)
        {
            // Real-time delta in 100 ns → convert to seconds → scale → world ticks
            long deltaUnix = earthUnixTicks - EarthEpochUnixTicks;
            double deltaWorldSeconds = (deltaUnix / 10_000_000.0) * TimeScale;
            return WorldEpochTicks + (long)(deltaWorldSeconds * _spec.TicksPerSecond);
        }

        /// <summary>
        /// Converts world ticks back into real time (100 ns UNIX ticks).
        /// </summary>
        /// <param name="worldTicks">The world ticks to convert.</param>
        /// <returns>The corresponding real time in 100 ns UNIX ticks.</returns>
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
        /// Returns the current system time as 100 ns UNIX ticks (since 1970-01-01Z).
        /// Funguje na Windows i Linuxu/macOS (POSIX).
        /// </summary>
        /// <exception cref="PlatformNotSupportedException">
        /// If the platform supports neither way of reading the system time.
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
        /// Converts Windows FILETIME (100 ns since 1601-01-01) into UNIX ticks (100 ns since 1970-01-01).
        /// </summary>
        private static long FileTimeToUnixTicks(FILETIME ft)
            => (((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime) - WindowsEpochFileTimeTicks;

        #endregion Privátní pomocné metody
    }
}
