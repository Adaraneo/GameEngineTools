// PersonalityReport.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using GameEngineTools.Characters.Traits;
    using System.Globalization;
    using System.Text;

    /// <summary>The personality part of a monitor report: every trait of every character, how varied the population is, which traits stand out per person and how traits relate to what happened in the run.</summary>
    public static class PersonalityReport
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>One numeric trait: where it is read from, and (for traits that drift) its value at the start of the run.</summary>
        private sealed record Trait(string Group, string Name, Func<PersonRecord, double?> Now, Func<PersonRecord, double?>? Start = null);

        private static Func<PersonRecord, double?> P(Func<Personality, double?> read) => p => p.Personality is { } x ? read(x) : null;

        private static readonly IReadOnlyList<Trait> Traits = BuildTraits();

        private static IReadOnlyList<Trait> BuildTraits()
        {
            var list = new List<Trait>
            {
                new("Big Five", "Openness", P(x => x.BigFive.Openness)),
                new("Big Five", "Conscientiousness", P(x => x.BigFive.Conscientiousness)),
                new("Big Five", "Extraversion", P(x => x.BigFive.Extraversion)),
                new("Big Five", "Agreeableness", P(x => x.BigFive.Agreeableness)),
                new("Big Five", "Neuroticism", P(x => x.BigFive.Neuroticism)),
                new("Attachment", "Anxiety", P(x => x.Attachment.Anxiety)),
                new("Attachment", "Avoidance", P(x => x.Attachment.Avoidance)),
                new("Drives", "Affiliation", P(x => x.Motivation.Affiliation)),
                new("Drives", "Achievement", P(x => x.Motivation.Achievement)),
                new("Drives", "Power", P(x => x.Motivation.Power)),
                new("Drives", "Altruism", P(x => x.Motivation.Altruism)),
                new("Drives", "Competence", P(x => x.Motivation.Competence)),
                new("Drives", "Autonomy", P(x => x.Motivation.Autonomy)),
                new("Drives", "Curiosity", P(x => x.Motivation.Curiosity)),
                new("Drives", "Rest", P(x => x.Motivation.Rest)),
                new("Drives", "Sexuality", P(x => x.Motivation.Sexuality)),
                new("Sociosexuality", "SOI-R behavior", P(x => x.Sociosexuality.Behavior)),
                new("Sociosexuality", "SOI-R attitude", P(x => x.Sociosexuality.Attitude)),
                new("Sociosexuality", "SOI-R desire", P(x => x.Sociosexuality.Desire)),
                new("Dual control", "SES", P(x => x.DualControl?.SES)),
                new("Dual control", "SIS1", P(x => x.DualControl?.SIS1)),
                new("Dual control", "SIS2", P(x => x.DualControl?.SIS2)),
                new("Cognition", "ToM ceiling", P(x => x.ToMCeiling)),
                new("Cognition", "Comparison (ability)", P(x => x.ComparisonOrientation?.Ability)),
                new("Cognition", "Comparison (opinion)", P(x => x.ComparisonOrientation?.Opinion)),
                new("Cognition", "Comparison (overall)", P(x => x.ComparisonOrientation?.Overall)),
                new("Cognition", "Discount rate k", P(x => x.TemporalDiscount?.K)),
                new("Motivation style", "Promotion focus", P(x => x.RegulatoryFocus?.Promotion)),
                new("Motivation style", "Prevention focus", P(x => x.RegulatoryFocus?.Prevention)),
                new("Motivation style", "Wanting", P(x => x.WantingSensitivity?.WantingSensitivity)),
                new("Motivation style", "Liking", P(x => x.WantingSensitivity?.LikingCapacity)),
                new("Threat", "FFFS sensitivity", P(x => x.FFFS?.Sensitivity)),
                new("Dark core", "D-factor", P(x => x.DarkCore?.DarkCore)),
                new("Dark core", "Justifying beliefs", P(x => x.DarkCore?.JustifyingBeliefs)),
            };

            AddValue(list, "Benevolence", v => v.Benevolence);
            AddValue(list, "Universalism", v => v.Universalism);
            AddValue(list, "Self-direction", v => v.SelfDirection);
            AddValue(list, "Stimulation", v => v.Stimulation);
            AddValue(list, "Hedonism", v => v.Hedonism);
            AddValue(list, "Achievement", v => v.Achievement);
            AddValue(list, "Power", v => v.Power);
            AddValue(list, "Security", v => v.Security);
            AddValue(list, "Conformity", v => v.Conformity);
            AddValue(list, "Tradition", v => v.Tradition);

            AddInterest(list, "Realistic", v => v.Realistic);
            AddInterest(list, "Investigative", v => v.Investigative);
            AddInterest(list, "Artistic", v => v.Artistic);
            AddInterest(list, "Social", v => v.Social);
            AddInterest(list, "Enterprising", v => v.Enterprising);
            AddInterest(list, "Conventional", v => v.Conventional);

            list.Add(new("Self-concept", "Self-esteem", p => p.SelfNow?.SelfEsteem, p => p.SelfStart?.SelfEsteem));
            list.Add(new("Self-concept", "Self-discrepancy", p => p.SelfNow?.SelfDiscrepancy, p => p.SelfStart?.SelfDiscrepancy));
            return list;
        }

        private static void AddValue(List<Trait> list, string name, Func<ValuesProfile, double> read) =>
            list.Add(new("Values", name, p => p.ValuesNow is { } v ? read(v) : null, p => p.ValuesStart is { } v ? read(v) : null));

        private static void AddInterest(List<Trait> list, string name, Func<InterestProfile, double> read) =>
            list.Add(new("Interests", name, p => p.InterestsNow is { } v ? read(v) : null, p => p.InterestsStart is { } v ? read(v) : null));

        /// <summary>Appends the personality sections to a report.</summary>
        public static void Append(StringBuilder sb, MonitorOptions options, Collector c)
        {
            if (c.People.Count == 0) return;

            var people = Ordered(c);
            var labels = people.Select((p, i) => $"{i + 1}. {p.Name} ({p.Occupation})").ToList();
            var lastDay = c.Samples.Count == 0 ? 0 : c.Samples[^1].Day;

            sb.AppendLine();
            sb.AppendLine($"## Personality ({people.Count} characters; stable traits as generated, values / interests / self-concept as they were at the end of the run, with the change since the start in brackets)");

            AppendSpread(sb, people);

            AppendTable(sb, "Temperament, attachment and daily rhythm", people, labels,
                Traits.Where(t => t.Group is "Big Five" or "Attachment").ToList(),
                extra: new[] { ("chronotype", (Func<PersonRecord, string>)(p => p.Personality?.Chronotype.ToString() ?? "-")), ("communication", p => p.Personality?.Communication.ToString() ?? "-") });
            AppendTable(sb, "Drives (relative weights)", people, labels, Traits.Where(t => t.Group == "Drives").ToList());
            AppendTable(sb, "Sexuality and closeness", people, labels, Traits.Where(t => t.Group is "Sociosexuality" or "Dual control").ToList());
            AppendTable(sb, "Cognition, motivation style, threat response and dark core", people, labels,
                Traits.Where(t => t.Group is "Cognition" or "Motivation style" or "Threat" or "Dark core").ToList());
            AppendTable(sb, "Values (Schwartz)", people, labels, Traits.Where(t => t.Group == "Values").ToList(), showDrift: true);
            AppendTable(sb, "Interests (RIASEC)", people, labels, Traits.Where(t => t.Group == "Interests").ToList(), showDrift: true);
            AppendTable(sb, "Self-concept", people, labels, Traits.Where(t => t.Group == "Self-concept").ToList(), showDrift: true);

            AppendDistinctive(sb, people, labels);
            AppendOutcomes(sb, people, lastDay);
        }

        /// <summary>One row per character with every trait and the headline outcomes, for a spreadsheet.</summary>
        public static string BuildCsv(Collector c)
        {
            var people = Ordered(c);
            var lastDay = c.Samples.Count == 0 ? 0 : c.Samples[^1].Day;
            var sb = new StringBuilder();
            sb.Append("name,sex,age,occupation,chronotype,communication");
            foreach (var t in Traits) sb.Append(',').Append(Csv(t.Group + " " + t.Name));
            foreach (var t in Traits.Where(t => t.Start is not null)) sb.Append(',').Append(Csv(t.Group + " " + t.Name + " change"));
            sb.AppendLine(",stress late mean,accepted,rejected,relationships,max closeness,invitations,died on day");
            foreach (var p in people)
            {
                sb.Append(Csv(p.Name)).Append(',').Append(p.Sex).Append(',').Append(p.Age).Append(',').Append(Csv(p.Occupation))
                    .Append(',').Append(p.Personality?.Chronotype.ToString() ?? "").Append(',').Append(p.Personality?.Communication.ToString() ?? "");
                foreach (var t in Traits) sb.Append(',').Append(Num(t.Now(p)));
                foreach (var t in Traits.Where(t => t.Start is not null))
                    sb.Append(',').Append(t.Now(p) is { } now && t.Start!(p) is { } start ? Num(now - start) : "");
                sb.Append(',').Append(Num(LateStress(p, lastDay))).Append(',').Append(p.Accepted).Append(',').Append(p.Rejected)
                    .Append(',').Append(p.Edges).Append(',').Append(Num(p.MaxCloseness)).Append(',').Append(p.Invites)
                    .Append(',').Append(p.DiedOnDay?.ToString(Inv) ?? "").AppendLine();
            }

            return sb.ToString().Replace("\r\n", "\n");
        }

        private static List<PersonRecord> Ordered(Collector c) => c.People.Values.ToList();

        private static double? LateStress(PersonRecord p, int lastDay)
        {
            var late = p.Stress.Where(x => x.Day * 2 >= lastDay).Select(x => x.Stress).ToList();
            return late.Count == 0 ? null : late.Average();
        }

        private static void AppendSpread(StringBuilder sb, IReadOnlyList<PersonRecord> people)
        {
            sb.AppendLine();
            sb.AppendLine("### Spread across the population (is everyone alike, or do the characters differ?)");
            sb.AppendLine("| group | trait | n | mean | SD | min | max |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (var t in Traits)
            {
                var values = Values(t, people);
                if (values.Count == 0) continue;
                sb.AppendLine($"| {t.Group} | {t.Name} | {values.Count} | {F(values.Average())} | {F(Sd(values))} | {F(values.Min())} | {F(values.Max())} |");
            }

            var chrono = people.Where(p => p.Personality is not null).GroupBy(p => p.Personality!.Chronotype).OrderBy(g => g.Key);
            var comm = people.Where(p => p.Personality is not null).GroupBy(p => p.Personality!.Communication).OrderBy(g => g.Key);
            sb.AppendLine();
            sb.AppendLine("Chronotype: " + string.Join(", ", chrono.Select(g => $"{g.Key} {g.Count()}")) + "; communication style: " + string.Join(", ", comm.Select(g => $"{g.Key} {g.Count()}")) + ".");
            var flat = Traits.Where(t => Values(t, people) is { Count: > 1 } v && Sd(v) < 0.02).Select(t => t.Name).ToList();
            if (flat.Count > 0)
                sb.AppendLine("Traits with almost no spread (SD under 0.02): " + string.Join(", ", flat) + ".");
        }

        private static void AppendTable(StringBuilder sb, string title, IReadOnlyList<PersonRecord> people, IReadOnlyList<string> labels, IReadOnlyList<Trait> traits,
            bool showDrift = false, IReadOnlyList<(string Name, Func<PersonRecord, string> Get)>? extra = null)
        {
            if (traits.Count == 0 || traits.All(t => Values(t, people).Count == 0)) return;

            sb.AppendLine();
            sb.AppendLine($"### {title}");
            sb.AppendLine("| character | " + string.Join(" | ", traits.Select(t => t.Name).Concat(extra?.Select(e => e.Name) ?? Array.Empty<string>())) + " |");
            sb.AppendLine("|---|" + string.Concat(traits.Select(_ => "---:|")) + string.Concat((extra ?? Array.Empty<(string, Func<PersonRecord, string>)>()).Select(_ => "---|")));
            for (var i = 0; i < people.Count; i++)
            {
                var cells = traits.Select(t =>
                {
                    if (t.Now(people[i]) is not { } now) return "-";
                    return showDrift && t.Start?.Invoke(people[i]) is { } start ? $"{F(now)} ({Signed(now - start)})" : F(now);
                }).Concat(extra?.Select(e => e.Get(people[i])) ?? Array.Empty<string>());
                sb.AppendLine($"| {labels[i]} | " + string.Join(" | ", cells) + " |");
            }
        }

        private static void AppendDistinctive(StringBuilder sb, IReadOnlyList<PersonRecord> people, IReadOnlyList<string> labels)
        {
            if (people.Count < 3) return;

            var stats = Traits.Select(t => (Trait: t, Values: Values(t, people))).Where(x => x.Values.Count == people.Count && Sd(x.Values) > 0.02)
                .Select(x => (x.Trait, Mean: x.Values.Average(), Sd: Sd(x.Values))).ToList();

            sb.AppendLine();
            sb.AppendLine("### What stands out about each character (traits at least 1.3 standard deviations from the population mean, strongest first; z in brackets)");
            sb.AppendLine("| character | stands out |");
            sb.AppendLine("|---|---|");
            for (var i = 0; i < people.Count; i++)
            {
                var marks = stats
                    .Select(s => (s.Trait, Z: ((s.Trait.Now(people[i]) ?? s.Mean) - s.Mean) / s.Sd))
                    .Where(x => Math.Abs(x.Z) >= 1.3)
                    .OrderByDescending(x => Math.Abs(x.Z)).Take(6)
                    .Select(x => $"{(x.Z > 0 ? "high" : "low")} {x.Trait.Name} ({Signed(x.Z, 1)})");
                var text = string.Join(", ", marks);
                sb.AppendLine($"| {labels[i]} | {(text.Length == 0 ? "nothing unusual" : text)} |");
            }
        }

        private static void AppendOutcomes(StringBuilder sb, IReadOnlyList<PersonRecord> people, int lastDay)
        {
            if (people.Count < 5) return;

            var outcomes = new (string Name, Func<PersonRecord, double?> Get)[]
            {
                ("stress (2nd half)", p => LateStress(p, lastDay)),
                ("accepted interactions", p => p.Accepted),
                ("relationships", p => p.Edges),
                ("max closeness", p => p.MaxCloseness),
                ("invitations tried", p => p.Invites),
            };

            sb.AppendLine();
            sb.AppendLine($"### Traits against what happened (Pearson r over {people.Count} characters; * = |r| of 0.5 or more; with so few characters treat it as a hint, not a result)");
            sb.AppendLine("| group | trait | " + string.Join(" | ", outcomes.Select(o => o.Name)) + " |");
            sb.AppendLine("|---|---|" + string.Concat(outcomes.Select(_ => "---:|")));
            foreach (var t in Traits)
            {
                var cells = outcomes.Select(o => Correlation(people, t.Now, o.Get)).ToList();
                if (cells.All(r => r is null)) continue;
                sb.AppendLine($"| {t.Group} | {t.Name} | " + string.Join(" | ", cells.Select(r => r is null ? "-" : $"{Signed(r.Value)}{(Math.Abs(r.Value) >= 0.5 ? "*" : "")}")) + " |");
            }
        }

        private static double? Correlation(IReadOnlyList<PersonRecord> people, Func<PersonRecord, double?> x, Func<PersonRecord, double?> y)
        {
            var pairs = people.Select(p => (X: x(p), Y: y(p))).Where(q => q.X is not null && q.Y is not null).Select(q => (X: q.X!.Value, Y: q.Y!.Value)).ToList();
            if (pairs.Count < 5) return null;
            var mx = pairs.Average(q => q.X);
            var my = pairs.Average(q => q.Y);
            var sxx = pairs.Sum(q => (q.X - mx) * (q.X - mx));
            var syy = pairs.Sum(q => (q.Y - my) * (q.Y - my));
            if (sxx < 1e-9 || syy < 1e-9) return null;
            return pairs.Sum(q => (q.X - mx) * (q.Y - my)) / Math.Sqrt(sxx * syy);
        }

        private static List<double> Values(Trait t, IReadOnlyList<PersonRecord> people) => people.Select(t.Now).Where(v => v is not null).Select(v => v!.Value).ToList();

        private static double Sd(IReadOnlyList<double> values)
        {
            if (values.Count < 2) return 0;
            var mean = values.Average();
            return Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        }

        private static string F(double v) => v.ToString("F2", Inv);

        private static string Signed(double v, int digits = 2) => (v >= 0 ? "+" : "") + v.ToString("F" + digits, Inv);

        private static string Num(double? v) => v is { } x ? x.ToString("0.####", Inv) : "";

        private static string Csv(string s) => s.Contains(',') || s.Contains('"') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
