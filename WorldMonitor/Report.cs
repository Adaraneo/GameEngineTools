// Report.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using GameEngineTools.Characters.Core;
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
            var level = options.Thresholds.ChronicStressLevel;
            sb.AppendLine($"| day | stress mean | median | p90 | max | above {level:F0} % | privacy % | at home % | asleep % | asleep with privacy % | awake with company % |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var (day, counters) in c.Days)
            {
                var stress = c.Samples.Where(s => s.Day == day).SelectMany(s => s.Stress).OrderBy(x => x).ToList();
                var row = stress.Count == 0 ? "| - | - | - | - | -" : $"| {stress.Average():F1} | {Percentile(stress, 0.5):F1} | {Percentile(stress, 0.9):F1} | {stress[^1]:F0} | {100.0 * stress.Count(x => x > level) / stress.Count:F1}";
                sb.AppendLine($"| {day} {row} | {Pct(counters.Private, counters.Total)} | {Pct(counters.AtHome, counters.Total)} | {Pct(counters.Asleep, counters.Total)} | {Pct(counters.AsleepPrivate, counters.Asleep)} | {Pct(counters.AwakeWithCompany, counters.Awake)} |");
            }

            sb.AppendLine();
            sb.AppendLine("## Food and water per world day (readings at every tick; meals and drinks are committed Eat/EatStored and Drink actions)");
            sb.AppendLine("| day | hunger mean | hunger 80+ % | thirst mean | thirst 80+ % | meals per character | drinks per character |");
            sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var (day, counters) in c.Days)
            {
                var people = Math.Max(1, c.CharactersSeen);
                var readings = Math.Max(1, counters.Total);
                sb.AppendLine($"| {day} | {counters.HungerSum / readings:F1} | {100.0 * counters.HungryReadings / readings:F1} | {counters.ThirstSum / readings:F1} | {100.0 * counters.ThirstyReadings / readings:F1} | {(double)counters.Meals / people:F1} | {(double)counters.Drinks / people:F1} |");
            }

            sb.AppendLine();
            sb.AppendLine("Trade and production actions committed: " + string.Join(", ", new[] { "Produce", "Process", "Sell", "Buy", "Barter", "Build", "EatStored" }.Select(a => $"{a} {c.Actions.GetValueOrDefault(a)}"))
                + $"; events Sold {c.Events.GetValueOrDefault("Sold")}, Bartered {c.Events.GetValueOrDefault("Bartered")}, SiteBuilt {c.Events.GetValueOrDefault("SiteBuilt")}.");
            if (c.FarmSamples.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Farms by 30-day period (grain in units; fields per stage F fallow, P ploughed, S sown, R ripe, H reaped; jobs are committed actions)");
                sb.AppendLine("| days | food store (end) | seed bin (end) | sheaves (end) | fields F/P/S/R/H (end) | plough | sow | reap | thresh | hunger 80+ % | meals per character |");
                sb.AppendLine("|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|");
                foreach (var period in c.FarmSamples.GroupBy(f => f.Day / 30).OrderBy(g => g.Key))
                {
                    var last = period.Last();
                    int Stage(GameEngineTools.World.Objects.Crops.FieldStage s) => last.Stages.GetValueOrDefault(s);
                    var days = c.Days.Where(d => d.Key / 30 == period.Key).Select(d => d.Value).ToList();
                    var readings = Math.Max(1, days.Sum(d => d.Total));
                    var people = Math.Max(1, c.CharactersSeen);
                    sb.AppendLine($"| {period.Key * 30}-{period.Key * 30 + 29} | {last.FoodUnits} | {last.SeedUnits} | {last.SheafUnits} | {Stage(GameEngineTools.World.Objects.Crops.FieldStage.Fallow)}/{Stage(GameEngineTools.World.Objects.Crops.FieldStage.Ploughed)}/{Stage(GameEngineTools.World.Objects.Crops.FieldStage.Sown)}/{Stage(GameEngineTools.World.Objects.Crops.FieldStage.Ripe)}/{Stage(GameEngineTools.World.Objects.Crops.FieldStage.Reaped)} | {days.Sum(d => d.Ploughed)} | {days.Sum(d => d.Sowed)} | {days.Sum(d => d.Reaped)} | {days.Sum(d => d.Threshed)} | {100.0 * days.Sum(d => d.HungryReadings) / readings:F1} | {(double)days.Sum(d => d.Meals) / people / Math.Max(1, days.Count):F1} |");
                }
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

            if (c.DeathLog.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("## Deaths");
                foreach (var d in c.DeathLog)
                    sb.AppendLine($"- day {d.Day}: {d.Who}, {d.Cause}; {d.Context}");
            }

            AppendCharacters(sb, options, c);
            AppendIntimacy(sb, c);
            AppendSituations(sb, c);
            AppendFriendship(sb, c);

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

        private static void AppendFriendship(StringBuilder sb, Collector c)
        {
            if (c.PairHours.Count == 0) return;

            // Closeness of a pair = the mean of the two directed edges (0 when neither character has an edge).
            double CloseOf(HumanId a, HumanId b)
            {
                var values = new List<double>();
                if (c.People.TryGetValue(a, out var pa) && pa.Closeness.TryGetValue(b, out var ab)) values.Add(ab);
                if (c.People.TryGetValue(b, out var pb) && pb.Closeness.TryGetValue(a, out var ba)) values.Add(ba);
                return values.Count == 0 ? 0 : values.Average();
            }

            var rows = c.PairHours.Select(kv => (Hours: kv.Value, Close: CloseOf(kv.Key.Item1, kv.Key.Item2))).ToList();
            sb.AppendLine();
            sb.AppendLine("## Time together and closeness (Hall 2018: about 50 h to a casual friend, 90 h to a friend, over 200 h to a close friend)");
            sb.AppendLine("| hours awake in the same place | pairs | closeness mean | median | share with closeness 30+ | share 55+ | share 80+ |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|");
            foreach (var (from, to) in new[] { (0.0, 5.0), (5.0, 15.0), (15.0, 30.0), (30.0, 50.0), (50.0, 90.0), (90.0, 1e9) })
            {
                var bucket = rows.Where(r => r.Hours >= from && r.Hours < to).Select(r => r.Close).OrderBy(x => x).ToList();
                var label = to > 1e8 ? $"{from:F0}+" : $"{from:F0}-{to:F0}";
                sb.AppendLine(bucket.Count == 0
                    ? $"| {label} | 0 | - | - | - | - | - |"
                    : $"| {label} | {bucket.Count} | {bucket.Average():F0} | {bucket[bucket.Count / 2]:F0} | {100.0 * bucket.Count(x => x >= 30) / bucket.Count:F0}% | {100.0 * bucket.Count(x => x >= 55) / bucket.Count:F0}% | {100.0 * bucket.Count(x => x >= 80) / bucket.Count:F0}% |");
            }
            sb.AppendLine();
            sb.AppendLine("Closest pairs (hours awake together, of them in a place with privacy):");
            sb.AppendLine("| pair | closeness | hours together | hours with privacy |");
            sb.AppendLine("|---|---:|---:|---:|");
            foreach (var kv in c.PairHours.OrderByDescending(kv => CloseOf(kv.Key.Item1, kv.Key.Item2)).Take(8))
                sb.AppendLine($"| {Name(c, kv.Key.Item1)} + {Name(c, kv.Key.Item2)} | {CloseOf(kv.Key.Item1, kv.Key.Item2):F0} | {kv.Value:F0} | {c.PrivatePairHours.GetValueOrDefault(kv.Key):F0} |");
        }

        private static string Name(Collector c, HumanId id) => c.People.TryGetValue(id, out var p) ? p.Name : id.Value.ToString()[..4];

        private static void AppendSituations(StringBuilder sb, Collector c)
        {
            if (c.Situations.Count == 0) return;

            var total = c.Situations.Values.Sum(v => v.N);
            sb.AppendLine();
            sb.AppendLine("## What awake characters do, by situation (ticks; plans are the running plan)");
            sb.AppendLine("| period | situation | share % | need belonging | need social | stress | valence | most common plans |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
            foreach (var (key, v) in c.Situations)
            {
                var parts = key.Split('|');
                var plans = string.Join(", ", v.Plans.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key} {100.0 * p.Value / v.N:F0}%"));
                sb.AppendLine($"| {parts[0]} | {parts[1]} | {100.0 * v.N / total:F1} | {v.Belonging / v.N:F0} | {v.NeedSocial / v.N:F0} | {v.Stress / v.N:F1} | {v.Valence / v.N:F2} | {plans} |");
            }
        }

        private static void AppendIntimacy(StringBuilder sb, Collector c)
        {
            if (c.People.Count == 0) return;

            sb.AppendLine();
            sb.AppendLine("## Intimacy (what the characters felt and tried; highest value seen during the run)");
            sb.AppendLine("Outcomes by speech act (seen at the speaker's tick; an invitation needs a private place and, to become an encounter, closeness 55-70 and interest 50-65 on the other side):");
            foreach (var (act, counts) in c.ActOutcomes)
                sb.AppendLine($"- {act}: accepted {counts.Accepted}, rejected {counts.Rejected}");
            sb.AppendLine($"- events: InteractionProposed {c.Events.GetValueOrDefault("InteractionProposed")}, SexualEncounterProposed {c.Events.GetValueOrDefault("SexualEncounterProposed")}, SexualEncounterOutcome {c.Events.GetValueOrDefault("SexualEncounterOutcome")}");
            sb.AppendLine();
            sb.AppendLine("The gate for starting an invitation needs closeness of 24-50 (lower with a more permissive SOI-R attitude), plus trust and comfort at the same scale for restricted characters.");
            sb.AppendLine("| name | sex | SOI-R attitude | need intimacy | sexual interest | intimate affinity | closeness | trust | comfort | invitations tried |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var p in c.People.Values.OrderByDescending(p => p.MaxCloseness))
                sb.AppendLine($"| {p.Name} | {p.Sex} | {p.SociosexualityAttitude:F2} | {p.MaxNeedIntimacy:F0} | {p.MaxSexualInterest:F0} | {p.MaxIntimateAffinity:F0} | {p.MaxCloseness:F0} | {p.MaxTrust:F0} | {p.MaxComfort:F0} | {p.Invites} |");
        }

        private static void AppendCharacters(StringBuilder sb, MonitorOptions options, Collector c)
        {
            if (c.People.Count == 0 || c.Samples.Count == 0) return;

            var level = options.Thresholds.ChronicStressLevel;
            var lastDay = c.Samples[^1].Day;
            var rows = c.People.Values
                .Select(p =>
                {
                    var late = p.Stress.Where(x => x.Day * 2 >= lastDay).Select(x => x.Stress).ToList();
                    return (Person: p, Mean: late.Count == 0 ? 0 : late.Average(), Share: late.Count == 0 ? 0 : 100.0 * late.Count(x => x > level) / late.Count);
                })
                .OrderByDescending(r => r.Share).ThenByDescending(r => r.Mean)
                .ToList();

            sb.AppendLine();
            sb.AppendLine($"## Characters (stress over the second half; * = above {level:F0} in at least half of the samples)");
            sb.AppendLine("| | name | sex | age | N | E | attach. anxiety | occupation | relationships | accepted / rejected | stress mean | above % |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---:|---|---:|---:|---:|---:|");
            foreach (var (p, mean, share) in rows)
                sb.AppendLine($"| {(p.DiedOnDay is { } died ? $"died day {died}" : share >= 50 ? "*" : "")} | {p.Name} | {p.Sex} | {p.Age} | {p.Neuroticism:F2} | {p.Extraversion:F2} | {p.AttachmentAnxiety:F2} | {p.Occupation} | {p.Edges} | {p.Accepted} / {p.Rejected} | {mean:F1} | {share:F0} |");

            var chronic = rows.Where(r => r.Share >= 50).Select(r => r.Person).ToList();
            var others = rows.Where(r => r.Share < 50).Select(r => r.Person).ToList();
            if (chronic.Count > 0 && others.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"Chronically stressed ({chronic.Count}) vs the rest ({others.Count}): neuroticism {chronic.Average(p => p.Neuroticism):F2} vs {others.Average(p => p.Neuroticism):F2}, extraversion {chronic.Average(p => p.Extraversion):F2} vs {others.Average(p => p.Extraversion):F2}, " +
                    $"relationships {chronic.Average(p => p.Edges):F1} vs {others.Average(p => p.Edges):F1}, rejection share {RejectionShare(chronic):F0}% vs {RejectionShare(others):F0}%.");
            }

            static double RejectionShare(IReadOnlyList<PersonRecord> people)
            {
                var total = people.Sum(p => p.Accepted + p.Rejected);
                return total == 0 ? 0 : 100.0 * people.Sum(p => p.Rejected) / total;
            }
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

            // A run that measured nothing must not pass: every check below would otherwise read an empty set as healthy.
            if (c.Ticks == 0 || c.Samples.Count == 0)
            {
                checks.Add(new Check("data collected", false, "no ticks or samples were recorded"));
                return checks;
            }

            if (limits.MaxMedianStress is { } maxStress)
            {
                // Judged on the second half of the run, after the characters have settled.
                var lastDay = c.Samples.Count == 0 ? 0 : c.Samples[^1].Day;
                var stress = c.Samples.Where(s => s.Day * 2 >= lastDay).SelectMany(s => s.Stress).OrderBy(x => x).ToList();
                var median = Percentile(stress, 0.5);
                checks.Add(new Check("median stress in the second half", stress.Count > 0 && median <= maxStress, $"{median:F1} (limit {maxStress})"));
            }

            if (limits.MaxChronicStressPercent is { } maxChronic)
            {
                // The median hides a stuck minority, so the second half is also judged by how many readings sit above the chronic level.
                var lastDay = c.Samples[^1].Day;
                var readings = c.Samples.Where(s => s.Day * 2 >= lastDay).SelectMany(s => s.Stress).ToList();
                var share = readings.Count == 0 ? 100.0 : 100.0 * readings.Count(x => x > limits.ChronicStressLevel) / readings.Count;
                checks.Add(new Check($"readings above {limits.ChronicStressLevel:F0} in the second half", readings.Count > 0 && share <= maxChronic, $"{share:F1}% (limit {maxChronic}%)"));
            }

            if (limits.MinSleepWithPrivacyPercent is { } minPrivacy)
            {
                var asleep = c.Days.Values.Sum(d => d.Asleep);
                var privacy = asleep == 0 ? 0.0 : 100.0 * c.Days.Values.Sum(d => d.AsleepPrivate) / asleep;
                checks.Add(new Check("sleep with privacy", asleep > 0 && privacy >= minPrivacy, $"{privacy:F1}% (limit {minPrivacy}%)"));
            }

            if (limits.MaxDeaths is { } maxDeaths)
                checks.Add(new Check("deaths", c.Deaths <= maxDeaths, $"{c.Deaths} (limit {maxDeaths})"));

            return checks;
        }

        private static double Percentile(IReadOnlyList<double> sorted, double p)
            => sorted.Count == 0 ? 0 : sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    }
}
