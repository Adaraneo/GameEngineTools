// Report.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using System.Globalization;
    using System.Text;

    /// <summary>Turns a <see cref="Collector"/> into a Markdown report, a CSV of the samples and pass/fail checks.</summary>
    public static class Report
    {
        public sealed record Check(string Name, bool Passed, string Detail);

        public static string Build(MonitorOptions options, Collector c, TimeSpan elapsed, IReadOnlyList<Check> checks)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("# World monitor report");
            sb.AppendLine();
            sb.AppendLine($"Profile **{options.ProfileName}**: {options.Run.Characters} characters, {options.Run.Days} world days, tick {options.Run.TickStepMinutes} min, substep {options.Run.SubstepMinutes} min; {c.Ticks} ticks in {elapsed.TotalMinutes:F1} min of real time.");
            sb.AppendLine();

            sb.AppendLine("## Per world day");
            sb.AppendLine("| day | stress mean | median | p90 | max | privacy % | at home % | asleep % | asleep with privacy % |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var (day, counters) in c.Days)
            {
                var stress = c.Samples.Where(s => s.Day == day).SelectMany(s => s.Stress).OrderBy(x => x).ToList();
                var row = stress.Count == 0 ? "| - | - | - | -" : $"| {stress.Average():F1} | {Percentile(stress, 0.5):F1} | {Percentile(stress, 0.9):F1} | {stress[^1]:F0}";
                sb.AppendLine($"| {day} {row} | {Pct(counters.Private, counters.Total)} | {Pct(counters.AtHome, counters.Total)} | {Pct(counters.Asleep, counters.Total)} | {Pct(counters.AsleepPrivate, counters.Asleep)} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Stress by time of day (24-hour clock)");
            sb.AppendLine("| hours | samples | mean | median | p90 |");
            sb.AppendLine("|---|---:|---:|---:|---:|");
            foreach (var (from, to) in new[] { (0, 6), (6, 12), (12, 18), (18, 24) })
            {
                var stress = c.Samples.Where(s => s.ClockHour24 >= from && s.ClockHour24 < to).SelectMany(s => s.Stress).OrderBy(x => x).ToList();
                sb.AppendLine(stress.Count == 0
                    ? $"| {from:00}-{to:00} | 0 | - | - | - |"
                    : $"| {from:00}-{to:00} | {stress.Count} | {stress.Average():F1} | {Percentile(stress, 0.5):F1} | {Percentile(stress, 0.9):F1} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Where sleep starts");
            foreach (var kv in c.SleepStarts.OrderByDescending(k => k.Value))
                sb.AppendLine($"- {kv.Key}: {kv.Value}");

            sb.AppendLine();
            sb.AppendLine("## Social life and population");
            sb.AppendLine($"- interactions accepted / rejected: {c.InteractionsAccepted} / {c.InteractionsRejected}");
            sb.AppendLine($"- sexual encounters: {c.SexualEncounters}, pregnancies: {c.Pregnancies}, births: {c.Births}, deaths: {c.Deaths}");
            if (c.Samples.Count > 0)
            {
                var first = c.Samples[0];
                var last = c.Samples[^1];
                sb.AppendLine($"- NeedSocial mean: {first.NeedSocial:F0} -> {last.NeedSocial:F0}; energy mean: {first.Energy:F0} -> {last.Energy:F0}; max interest drift: {last.InterestDrift:F3}");
            }

            sb.AppendLine();
            sb.AppendLine("## Most frequent actions and places (seen at tick boundaries)");
            sb.AppendLine("- actions: " + string.Join(", ", c.Actions.OrderByDescending(a => a.Value).Take(10).Select(a => $"{a.Key} {a.Value}")));
            var total = Math.Max(1, c.LocationTicks.Values.Sum());
            sb.AppendLine("- places: " + string.Join(", ", c.LocationTicks.OrderByDescending(a => a.Value).Take(10).Select(a => $"{a.Key} {100.0 * a.Value / total:F1}%")));

            if (checks.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Checks");
                foreach (var check in checks)
                    sb.AppendLine($"- **{(check.Passed ? "PASS" : "FAIL")}** {check.Name}: {check.Detail}");
            }

            return sb.ToString().Replace("\r\n", "\n");

            string Pct(long part, long whole) => whole == 0 ? "-" : (100.0 * part / whole).ToString("F1", inv);
        }

        public static string BuildCsv(Collector c)
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("day,clockHour24,stressMean,stressMedian,stressP90,stressMax,privacy,atHome,asleep,needSocial,energy,interestDrift\n");
            foreach (var s in c.Samples)
                sb.AppendLine(string.Join(',', s.Day, s.ClockHour24.ToString("F1", inv), s.Stress.Average().ToString("F2", inv), Percentile(s.Stress, 0.5).ToString("F2", inv), Percentile(s.Stress, 0.9).ToString("F2", inv),
                    s.Stress[^1].ToString("F1", inv), s.Private, s.AtHome, s.Asleep, s.NeedSocial.ToString("F1", inv), s.Energy.ToString("F1", inv), s.InterestDrift.ToString("F4", inv)));
            return sb.ToString();
        }

        public static IReadOnlyList<Check> Evaluate(Thresholds limits, Collector c)
        {
            var checks = new List<Check>();

            if (limits.MaxMedianStress is { } maxStress)
            {
                // Judged on the second half of the run, after the characters have settled.
                var lastDay = c.Samples.Count == 0 ? 0 : c.Samples[^1].Day;
                var stress = c.Samples.Where(s => s.Day * 2 >= lastDay).SelectMany(s => s.Stress).OrderBy(x => x).ToList();
                var median = stress.Count == 0 ? 0 : Percentile(stress, 0.5);
                checks.Add(new Check("median stress in the second half", median <= maxStress, $"{median:F1} (limit {maxStress})"));
            }

            if (limits.MinSleepWithPrivacyPercent is { } minPrivacy)
            {
                var asleep = c.Days.Values.Sum(d => d.Asleep);
                var privacy = asleep == 0 ? 100.0 : 100.0 * c.Days.Values.Sum(d => d.AsleepPrivate) / asleep;
                checks.Add(new Check("sleep with privacy", privacy >= minPrivacy, $"{privacy:F1}% (limit {minPrivacy}%)"));
            }

            if (limits.MaxDeaths is { } maxDeaths)
                checks.Add(new Check("deaths", c.Deaths <= maxDeaths, $"{c.Deaths} (limit {maxDeaths})"));

            return checks;
        }

        private static double Percentile(IReadOnlyList<double> sorted, double p)
            => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    }
}
