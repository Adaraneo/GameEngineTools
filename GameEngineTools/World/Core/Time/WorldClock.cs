using System.Runtime.InteropServices;
using GameEngineTools.World.Utils.Time;

namespace GameEngineTools.World.Core.Time
{
    /// <summary>
    /// Lineární mapování reálného času (Earth UNIX ticks = 100ns od 1970-01-01Z)
    /// na světový čas (worldTicks podle WorldTimeSpec).
    /// </summary>
    public sealed class WorldClock : IWorldClock
    {
        // ---- OS čas (Windows/Posix) v 100ns UNIX tickách ----
        private const long WindowsEpochFileTimeTicks = 116_444_736_000_000_000L;

        [DllImport("libc", EntryPoint = "clock_gettime")]
        private static extern int clock_gettime(int clk_id, out timespec ts);

        [DllImport("kernel32.dll")]
        private static extern void GetSystemTimeAsFileTime(out FILETIME lpSystemTimeAsFileTime);

        [DllImport("kernel32.dll", EntryPoint = "GetSystemTimePreciseAsFileTime")]
        private static extern void GetSystemTimePreciseAsFileTime(out FILETIME lpSystemTimeAsFileTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        { public uint dwLowDateTime; public uint dwHighDateTime; }

        // 1601->1970 (100ns)
        [StructLayout(LayoutKind.Sequential)]
        private struct timespec
        { public long tv_sec; public long tv_nsec; }

        public WorldClock(WorldTimeSpec spec, long earthEpochUnixTicks, long worldEpochTicks, double timeScale = 1.0)
        {
            Spec = spec;
            EarthEpochUnixTicks = earthEpochUnixTicks;
            WorldEpochTicks = worldEpochTicks;
            TimeScale = timeScale;
        }

        public long EarthEpochUnixTicks { get; }
        public WorldTimeSpec Spec { get; }
        public double TimeScale { get; }

        // 100ns ticks (UNIX)
        public long WorldEpochTicks { get; }     // worldTicks odpovídající EarthEpochUnixTicks

        /// <summary>
        /// Zarovná tak, aby v reálném čase 'earthAnchorUnixTicks' byl ve světě 'worldAtAnchor'.
        /// </summary>
        public static WorldClock AlignAt(WorldTimeSpec spec, long earthAnchorUnixTicks, WDateTime worldAtAnchor, double timeScale = 1.0)
            => new(spec, earthAnchorUnixTicks, worldAtAnchor.WorldTicks, timeScale);

        /// <summary>
        /// Zarovná “teď” na zadaný světový okamžik (např. 321-01-01 00:00), při zadané rychlosti.
        /// </summary>
        public static WorldClock AlignNow(WorldTimeSpec spec, WDateTime worldNow, double timeScale = 1.0)
            => new(spec, SystemUnixTicks(), worldNow.WorldTicks, timeScale);

        public static long SystemUnixTicks()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    GetSystemTimePreciseAsFileTime(out var ft);
                    return (((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime) - WindowsEpochFileTimeTicks;
                }
                catch
                {
                    GetSystemTimeAsFileTime(out var ft);
                    return (((long)ft.dwHighDateTime << 32) | ft.dwLowDateTime) - WindowsEpochFileTimeTicks;
                }
            }
            else
            {
                if (clock_gettime(0, out var ts) == 0)
                    return ts.tv_sec * 10_000_000L + (ts.tv_nsec / 100); // ns -> 100ns
            }
            throw new PlatformNotSupportedException("Nepodařilo se získat systémový čas.");
        }

        // ---- Konverze Earth <-> World ----
        public long EarthToWorldTicks(long earthUnixTicks)
        {
            long deltaUnix = earthUnixTicks - EarthEpochUnixTicks; // 100ns ticks
            double deltaWorldSeconds = (deltaUnix / 10_000_000.0) * TimeScale;
            return WorldEpochTicks + (long)(deltaWorldSeconds * Spec.TicksPerSecond);
        }

        // 1.0 => 1 světová sekunda == 1 reálná sekunda
        // ---- “Teď” v worldTickách ----
        public long NowWorldTicks()
        {
            long unixNow = SystemUnixTicks();
            return EarthToWorldTicks(unixNow);
        }

        public long WorldToEarthUnixTicks(long worldTicks)
        {
            long deltaWorldTicks = worldTicks - WorldEpochTicks;
            double deltaWorldSeconds = deltaWorldTicks / (double)Spec.TicksPerSecond;
            long deltaUnix = (long)(deltaWorldSeconds / TimeScale * 10_000_000.0);
            return EarthEpochUnixTicks + deltaUnix;
        }

        // CLOCK_REALTIME = 0
    }
}