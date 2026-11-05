// Progress.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using System.Diagnostics;

    /// <summary>Prints how far a run has got: a bar on a terminal, one line per world day when output is redirected.</summary>
    public sealed class Progress
    {
        private readonly int _totalDays;
        private readonly Stopwatch _clock;
        private readonly bool _interactive = !Console.IsOutputRedirected;
        private long _lastPrintMs = -1000;
        private int _lastDay = -1;

        public Progress(int totalDays, Stopwatch clock)
        {
            _totalDays = totalDays;
            _clock = clock;
        }

        /// <summary>Call once per tick with the elapsed world days and the latest stress mean, if any.</summary>
        public void Update(double worldDays, double? stressMean, long ticks)
        {
            var day = (int)worldDays;
            var elapsedMs = _clock.ElapsedMilliseconds;
            var newDay = day != _lastDay;
            if (!newDay && (!_interactive || elapsedMs - _lastPrintMs < 500)) return;

            _lastDay = day;
            _lastPrintMs = elapsedMs;

            var fraction = Math.Clamp(worldDays / _totalDays, 0.0, 1.0);
            var eta = fraction > 0.01 ? TimeSpan.FromMilliseconds(elapsedMs / fraction - elapsedMs) : (TimeSpan?)null;
            var stress = stressMean is { } s ? $"  stress mean {s:F1}" : string.Empty;
            var text = $"{Bar(fraction)} day {Math.Min(day + 1, _totalDays)}/{_totalDays} ({fraction * 100:F0}%)  elapsed {Format(TimeSpan.FromMilliseconds(elapsedMs))}  ETA {(eta is { } e ? Format(e) : "--:--")}{stress}  ticks {ticks}";

            if (_interactive)
                Console.Write("\r" + text.PadRight(Math.Max(text.Length, Console.WindowWidth - 1)));
            else
                Console.WriteLine(text);
        }

        /// <summary>Ends the progress line so the report starts on a fresh one.</summary>
        public void Finish()
        {
            if (_interactive) Console.WriteLine();
        }

        private static string Bar(double fraction)
        {
            const int width = 24;
            var filled = (int)(fraction * width);
            return "[" + new string('#', filled) + new string('.', width - filled) + "]";
        }

        private static string Format(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }
}
