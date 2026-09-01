using System.Diagnostics;

namespace TerrainEditor.Diagnostics;

/// <summary>
/// Lightweight, always-on timing/event log for the continuous-tile-panning pipeline (coverage
/// checks, background stitching, grid/overlay rendering) — lets <see cref="PerfLogWindow"/> show
/// what's actually happening and how long each step takes, instead of guessing whether a given
/// optimization helped. Recording an entry is cheap (a timestamp, a list add, an event invoke) so
/// it's safe to leave instrumented call sites in place permanently.
/// </summary>
public static class PerfLog
{
    /// <param name="Timestamp">Wall-clock time the entry was recorded.</param>
    /// <param name="Category">Short source tag, e.g. "Coverage", "Stitch", "RenderGrid".</param>
    /// <param name="Message">Human-readable detail — tile counts, grid dimensions, cache hit/miss, etc.</param>
    /// <param name="DurationMs">Elapsed time for a timed <see cref="Scope"/>, or <c>null</c> for a
    /// plain point-in-time event.</param>
    /// <param name="ManagedMemoryBytes">GC-tracked managed heap size at the moment of logging.</param>
    /// <param name="WorkingSetBytes">Process working set at the moment of logging.</param>
    public sealed record Entry(
        DateTime Timestamp, string Category, string Message, double? DurationMs,
        long ManagedMemoryBytes, long WorkingSetBytes);

    private const int MaxEntries = 1000;
    private static readonly object Sync = new();
    private static readonly List<Entry> Entries = new(MaxEntries);

    /// <summary>Raised on every new entry — <see cref="PerfLogWindow"/> subscribes to append live
    /// instead of polling. Fired from whatever thread called <see cref="Log"/>; the window
    /// marshals to the UI thread itself.</summary>
    public static event Action<Entry>? EntryAdded;

    public static void Log(string category, string message, double? durationMs = null)
    {
        var entry = new Entry(DateTime.Now, category, message, durationMs,
            GC.GetTotalMemory(false), Process.GetCurrentProcess().WorkingSet64);

        lock (Sync)
        {
            Entries.Add(entry);
            if (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }

        EntryAdded?.Invoke(entry);
    }

    /// <summary>Snapshot of every entry currently retained (oldest first) — used to seed a newly
    /// opened <see cref="PerfLogWindow"/> with history instead of starting empty.</summary>
    public static IReadOnlyList<Entry> Snapshot()
    {
        lock (Sync)
            return Entries.ToList();
    }

    public static void Clear()
    {
        lock (Sync)
            Entries.Clear();
    }

    /// <summary>Times a block of code and logs it on disposal — <c>using (PerfLog.Scope("RenderGrid", "..."))</c>.</summary>
    public static IDisposable Scope(string category, string message) => new TimedScope(category, message);

    private sealed class TimedScope : IDisposable
    {
        private readonly string _category;
        private readonly string _message;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

        public TimedScope(string category, string message)
        {
            _category = category;
            _message = message;
        }

        public void Dispose() => Log(_category, _message, _stopwatch.Elapsed.TotalMilliseconds);
    }
}
