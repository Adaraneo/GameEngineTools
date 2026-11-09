// Collector.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.Characters.Engines.SelfConcept;
    using GameEngineTools.Characters.Traits;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>The farms at one moment: grain in the food stores and seed bins, unthreshed grain in sheaves, and how many fields are in each stage.</summary>
    public sealed record FarmSample(int Day, long FoodUnits, long SeedUnits, long SheafUnits, IReadOnlyDictionary<GameEngineTools.World.Objects.Crops.FieldStage, int> Stages);

    /// <summary>The stress of everyone at one moment of the run.</summary>
    public sealed record Sample(int Day, double ClockHour24, IReadOnlyList<double> Stress, int Private, int AtHome, int Asleep, double NeedSocial, double Energy, double InterestDrift);

    /// <summary>What the monitor knows about one character: who they are and how their stress and social life went.</summary>
    public sealed class PersonRecord
    {
        public string Name = "";
        public string Sex = "";
        public int Age;
        public double Neuroticism, Extraversion, AttachmentAnxiety;
        public string Occupation = "-";
        public string Home = "-";
        public int Edges;
        public int Accepted, Rejected;
        public int? DiedOnDay;
        public Dictionary<HumanId, double> Closeness { get; } = new();
        public double MaxNeedIntimacy, MaxSexualInterest, MaxIntimateAffinity, MaxCloseness, MaxTrust, MaxComfort;
        public double SociosexualityAttitude;
        public Personality? Personality;
        public ValuesProfile? ValuesStart, ValuesNow;
        public InterestProfile? InterestsStart, InterestsNow;
        public SelfConcept? SelfStart, SelfNow;
        public int Invites;
        public double LastHunger, LastThirst, MaxHunger, MaxThirst;
        public string LastLocation = "?";
        public string LastAction = "-";
        public List<string> History { get; } = new();
        public string LastHistoryKey = "";
        public string LastBody = "";
        public List<(int Day, double Stress)> Stress { get; } = new();
    }

    /// <summary>How awake characters in one situation (and half of the run) behaved and felt.</summary>
    public sealed class SituationStats
    {
        public long N;
        public double Belonging, NeedSocial, Stress, Valence;
        public Dictionary<string, long> Plans { get; } = new();
    }

    /// <summary>One death seen during the run.</summary>
    public sealed record DeathRecord(int Day, string Who, string Cause, string Context);

    /// <summary>Per-world-day counters taken at every tick.</summary>
    public sealed class DayCounters
    {
        public long Total, Private, AtHome, Asleep, AsleepPrivate, Awake, AwakeWithCompany, HungryReadings, ThirstyReadings, Meals, Drinks, Ploughed, Sowed, Reaped, Threshed;
        public double HungerSum, ThirstSum;
    }

    /// <summary>Gathers the measurements of a run; <see cref="Record"/> is called once per scene tick.</summary>
    public sealed class Collector
    {
        private readonly double _sampleEveryHours;
        private WDateTime? _start;
        private long _lastSampleSlot = -1;
        private readonly Dictionary<HumanId, bool> _wasAsleep = new();
        private readonly HashSet<HumanId> _dead = new();
        private readonly HashSet<(long, HumanId, HumanId)> _seenOutcomes = new();
        private readonly HashSet<(long, HumanId, Type)> _seenOther = new();

        private readonly int _totalDays;
        private readonly double _hungerLevel;
        private readonly int _farmPeriodDays;

        public Collector(double sampleEveryHours, int totalDays = 0, double hungerLevel = 80, int farmPeriodDays = 30)
        {
            _sampleEveryHours = sampleEveryHours;
            _hungerLevel = hungerLevel;
            _farmPeriodDays = farmPeriodDays;
            _totalDays = totalDays;
        }

        /// <summary>Key "first half|home alone" and so on; the halves split the run at its middle.</summary>
        public SortedDictionary<string, SituationStats> Situations { get; } = new();

        public List<Sample> Samples { get; } = new();
        public Dictionary<HumanId, PersonRecord> People { get; } = new();
        public List<DeathRecord> DeathLog { get; } = new();

        /// <summary>Hours two characters were awake in the same place (sampled at tick boundaries), by pair.</summary>
        public Dictionary<(HumanId, HumanId), double> PairHours { get; } = new();

        public Dictionary<(HumanId, HumanId), double> PrivatePairHours { get; } = new();

        private WDateTime? _lastTickTime;
        public SortedDictionary<int, DayCounters> Days { get; } = new();
        public Dictionary<string, long> LocationTicks { get; } = new();
        public Dictionary<string, int> SleepStarts { get; } = new();
        public Dictionary<string, long> Actions { get; } = new();
        public Dictionary<string, long> Events { get; } = new();

        /// <summary>Accepted and rejected interactions by speech act; invitations are split by whether the speaker stood somewhere private.</summary>
        public SortedDictionary<string, (int Accepted, int Rejected)> ActOutcomes { get; } = new();
        public int InteractionsAccepted { get; private set; }
        public int InteractionsRejected { get; private set; }
        public int SexualEncounters { get; private set; }
        public int Pregnancies { get; private set; }
        public int Births { get; private set; }
        public int Deaths { get; private set; }
        public long Ticks { get; private set; }
        public int CharactersSeen { get; private set; }

        /// <summary>World days elapsed at the latest tick.</summary>
        public double WorldDays { get; private set; }

        /// <summary>Returns every world object (set by the host when farms are wanted in the report).</summary>
        public Func<IEnumerable<GameEngineTools.World.Objects.WorldObject>>? FarmSampler { get; set; }

        /// <summary>Hunger and thirst level counted as "high" in the food tables.</summary>
        public double HungerLevel => _hungerLevel;

        /// <summary>Length in world days of one row of the farms table.</summary>
        public int FarmPeriodDays => _farmPeriodDays;

        public List<FarmSample> FarmSamples { get; } = new();

        private FarmSample TakeFarm(int day)
        {
            var objects = FarmSampler!().ToList();
            var stages = objects.Where(o => o.Field is not null).GroupBy(o => o.Field!.Stage).ToDictionary(g => g.Key, g => g.Count());
            return new FarmSample(
                day,
                objects.Where(GameEngineTools.World.Objects.Crops.GrainStores.IsFoodStore).Sum(o => (long)o.Quantity),
                objects.Where(GameEngineTools.World.Objects.Crops.GrainStores.IsSeedBin).Sum(o => (long)o.Quantity),
                objects.Where(o => o.Field is not null).Sum(o => (long)o.Field!.PendingUnits),
                stages);
        }

        public void Record(WDateTime now, IReadOnlyList<IHuman> characters)
        {
            _start ??= now;
            Ticks++;
            CharactersSeen = Math.Max(CharactersSeen, characters.Count);

            var hours = WDateTime.Difference(now, _start.Value).TotalHours;
            WorldDays = hours / WWorld.Spec.HoursPerDay;
            var day = (int)WorldDays;
            if (!Days.TryGetValue(day, out var counters))
                Days[day] = counters = new DayCounters();

            var peopleAt = characters
                .Where(h => !_dead.Contains(h.Id))
                .GroupBy(h => h.Snapshot.InteractionSurface.Location ?? "?")
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var c in characters)
            {
                if (!People.TryGetValue(c.Id, out var person)) People[c.Id] = person = Describe(c);

                // A character that has died stays in the roster with its last state frozen; it must not feed the statistics.
                if (_dead.Contains(c.Id)) continue;

                var ph = c.Snapshot.Physiology;
                person.LastHunger = ph.Hunger;
                person.LastThirst = ph.Thirst;
                person.MaxHunger = Math.Max(person.MaxHunger, ph.Hunger);
                person.MaxThirst = Math.Max(person.MaxThirst, ph.Thirst);
                person.MaxNeedIntimacy = Math.Max(person.MaxNeedIntimacy, c.Snapshot.Behavior.NeedIntimacy);
                foreach (var edge in c.Snapshot.Relationships.Edges.Values)
                {
                    person.MaxSexualInterest = Math.Max(person.MaxSexualInterest, edge.SexualInterest);
                    person.MaxIntimateAffinity = Math.Max(person.MaxIntimateAffinity, edge.IntimateAffinity);
                    person.MaxCloseness = Math.Max(person.MaxCloseness, edge.Closeness);
                    person.MaxTrust = Math.Max(person.MaxTrust, edge.Trust);
                    person.MaxComfort = Math.Max(person.MaxComfort, edge.Comfort);
                }

                var surface = c.Snapshot.InteractionSurface;
                var location = surface.Location ?? "?";
                person.LastLocation = location;
                person.LastBody = $"allostatic {ph.AllostaticLoad:F0}, immune {ph.ImmuneLoad:F0}, energy {ph.Energy:F0}, sleep debt {ph.SleepDebtHours:F1} h";
                var historyKey = $"{location}|{c.Snapshot.Behavior.CurrentPlan?.Name ?? "(none)"}|{c.Snapshot.Behavior.ActiveSleep is not null}";
                if (historyKey != person.LastHistoryKey)
                {
                    person.LastHistoryKey = historyKey;
                    person.History.Add($"day {day} {(hours % WWorld.Spec.HoursPerDay):F0}h {historyKey} thirst {ph.Thirst:F0} hunger {ph.Hunger:F0}");
                    if (person.History.Count > 14) person.History.RemoveAt(0);
                }
                var asleep = c.Snapshot.Behavior.ActiveSleep is not null;
                var atHome = c.Identity.HomeLocationId == location;

                counters.Total++;
                counters.HungerSum += ph.Hunger;
                counters.ThirstSum += ph.Thirst;
                if (ph.Hunger >= _hungerLevel) counters.HungryReadings++;
                if (ph.Thirst >= _hungerLevel) counters.ThirstyReadings++;
                if (surface.HasPrivacy) counters.Private++;
                if (atHome) counters.AtHome++;
                if (asleep) { counters.Asleep++; if (surface.HasPrivacy) counters.AsleepPrivate++; }
                else
                {
                    counters.Awake++;
                    var withCompany = peopleAt.GetValueOrDefault(location) > 1;
                    if (withCompany) counters.AwakeWithCompany++;
                    NoteSituation(c, day, atHome, withCompany);
                }
                LocationTicks[location] = LocationTicks.GetValueOrDefault(location) + 1;

                NoteSleepStart(c, asleep, atHome, surface.HasPrivacy);
                CountEvents(c);
            }

            var tickHours = _lastTickTime is { } previous ? WDateTime.Difference(now, previous).TotalHours : 0.0;
            _lastTickTime = now;
            if (tickHours > 0)
            {
                foreach (var group in characters
                    .Where(h => !_dead.Contains(h.Id) && h.Snapshot.Behavior.ActiveSleep is null)
                    .GroupBy(h => h.Snapshot.InteractionSurface.Location ?? "?"))
                {
                    var here = group.Select(h => h.Id).ToList();
                    for (var i = 0; i < here.Count; i++)
                        for (var j = i + 1; j < here.Count; j++)
                        {
                            var key = here[i].Value.CompareTo(here[j].Value) < 0 ? (here[i], here[j]) : (here[j], here[i]);
                            PairHours[key] = PairHours.GetValueOrDefault(key) + tickHours;
                            if (group.First().Snapshot.InteractionSurface.HasPrivacy) PrivatePairHours[key] = PrivatePairHours.GetValueOrDefault(key) + tickHours;
                        }
                }
            }

            var slot = (long)(hours / _sampleEveryHours);
            if (slot != _lastSampleSlot && characters.Count > 0)
            {
                _lastSampleSlot = slot;
                Samples.Add(Take(day, now, characters));
                if (FarmSampler is not null)
                    FarmSamples.Add(TakeFarm(day));
                UpdatePeople(day, characters);
            }
        }

        private void NoteSituation(IHuman c, int day, bool atHome, bool withCompany)
        {
            var half = _totalDays > 0 && day * 2 >= _totalDays ? "second half" : "first half";
            var key = $"{half}|{(atHome ? "home" : "away")} {(withCompany ? "with company" : "alone")}";
            if (!Situations.TryGetValue(key, out var stats)) Situations[key] = stats = new SituationStats();

            var b = c.Snapshot.Behavior;
            var plan = b.CurrentPlan?.Name ?? "(none)";
            stats.N++;
            stats.Belonging += b.NeedBelonging;
            stats.NeedSocial += c.Snapshot.Psychology.Motivations?.NeedSocial ?? 0;
            stats.Stress += c.Snapshot.Psychology.Stress;
            stats.Valence += c.Snapshot.Psychology.Valence;
            stats.Plans[plan] = stats.Plans.GetValueOrDefault(plan) + 1;
        }

        private void NoteSleepStart(IHuman c, bool asleep, bool atHome, bool hasPrivacy)
        {
            var wasAsleep = _wasAsleep.GetValueOrDefault(c.Id);
            _wasAsleep[c.Id] = asleep;
            if (!asleep || wasAsleep) return;

            var needRest = c.Snapshot.Behavior.NeedRest;
            var key = $"{(atHome ? "home" : hasPrivacy ? "private elsewhere" : "public place")}, {(needRest >= 90 ? "NeedRest>=90" : "NeedRest<90")}";
            SleepStarts[key] = SleepStarts.GetValueOrDefault(key) + 1;
        }

        private void CountEvents(IHuman c)
        {
            foreach (var e in c.LastOutbox)
            {
                var name = e.GetType().Name;
                Events[name] = Events.GetValueOrDefault(name) + 1;

                switch (e)
                {
                    case ActionCommitted ac:
                        Actions[ac.ActionName] = Actions.GetValueOrDefault(ac.ActionName) + 1;
                        if (Days.TryGetValue((int)WorldDays, out var today))
                        {
                            if (ac.ActionName is ActionNames.Eat or ActionNames.EatStored) today.Meals++;
                            else if (ac.ActionName == ActionNames.Drink) today.Drinks++;
                            else if (ac.ActionName == ActionNames.Plough) today.Ploughed++;
                            else if (ac.ActionName == ActionNames.Sow) today.Sowed++;
                            else if (ac.ActionName == ActionNames.Reap) today.Reaped++;
                            else if (ac.ActionName == ActionNames.Thresh) today.Threshed++;
                        }
                        if (People.TryGetValue(ac.Human, out var actor)) { actor.LastAction = ac.ActionName; if (ac.ActionName == ActionNames.InviteIntimacy) actor.Invites++; }
                        break;
                    case InteractionOutcome io when _seenOutcomes.Add((io.OccurredAt.WorldTicks, io.From, io.To)):
                        if (io.Accepted) InteractionsAccepted++; else InteractionsRejected++;
                        var actKey = io.Act == GameEngineTools.Dialogue.Contracts.RelationalActKind.Invite
                            ? $"Invite ({(c.Snapshot.InteractionSurface.HasPrivacy ? "private place" : "no privacy")})"
                            : io.Act.ToString();
                        var counts = ActOutcomes.GetValueOrDefault(actKey);
                        ActOutcomes[actKey] = io.Accepted ? (counts.Accepted + 1, counts.Rejected) : (counts.Accepted, counts.Rejected + 1);
                        if (People.TryGetValue(io.From, out var sender)) { if (io.Accepted) sender.Accepted++; else sender.Rejected++; }
                        break;
                    case SexualEncounterOutcome se when se.Accepted && _seenOther.Add((se.OccurredAt.WorldTicks, se.From, e.GetType())):
                        SexualEncounters++;
                        break;
                    case PregnancyStarted ps when _seenOther.Add((ps.OccurredAt.WorldTicks, ps.Human, e.GetType())):
                        Pregnancies++;
                        break;
                    case ChildBorn cb when _seenOther.Add((cb.OccurredAt.WorldTicks, cb.ParentA, e.GetType())):
                        Births++;
                        break;
                    case CharacterDied cd when _seenOther.Add((cd.OccurredAt.WorldTicks, cd.VictimId, e.GetType())):
                        Deaths++;
                        _dead.Add(cd.VictimId);
                        if (People.TryGetValue(cd.VictimId, out var victim))
                        {
                            victim.DiedOnDay = (int)WorldDays;
                            DeathLog.Add(new DeathRecord((int)WorldDays, $"{victim.Name} ({victim.Sex}, {victim.Age}, {victim.Occupation})", cd.Cause.ToString(),
                                $"last at {victim.LastLocation} doing {victim.LastAction}; hunger {victim.LastHunger:F0} (max {victim.MaxHunger:F0}), thirst {victim.LastThirst:F0} (max {victim.MaxThirst:F0}), stress {victim.Stress.LastOrDefault().Stress:F0}; {victim.LastBody}" + " | history: " + string.Join(" ; ", victim.History)));
                        }
                        else
                        {
                            DeathLog.Add(new DeathRecord((int)WorldDays, "?", cd.Cause.ToString(), "unknown character"));
                        }

                        break;
                }
            }
        }

        private static PersonRecord Describe(IHuman c) => new()
        {
            Name = c.Identity.FirstName.Original,
            Sex = c.Biology.ToString(),
            Age = c.Age,
            Neuroticism = c.Personality.BigFive.Neuroticism,
            Extraversion = c.Personality.BigFive.Extraversion,
            AttachmentAnxiety = c.Personality.Attachment.Anxiety,
            SociosexualityAttitude = c.Personality.Sociosexuality.Attitude,
            Personality = c.Personality,
            ValuesStart = c.Snapshot.Values?.Current,
            InterestsStart = c.Snapshot.Interests?.Current,
            SelfStart = c.Snapshot.SelfConcept,
        };

        private void UpdatePeople(int day, IReadOnlyList<IHuman> characters)
        {
            foreach (var c in characters)
            {
                if (_dead.Contains(c.Id)) continue;
                if (!People.TryGetValue(c.Id, out var person)) People[c.Id] = person = Describe(c);
                person.Age = c.Age;
                person.Occupation = c.Snapshot.Schedule?.Occupation is { Length: > 0 } occupation ? occupation : "-";
                person.Home = c.Identity.HomeLocationId ?? "-";
                person.Edges = c.Snapshot.Relationships.Edges.Count;
                person.ValuesNow = c.Snapshot.Values?.Current;
                person.InterestsNow = c.Snapshot.Interests?.Current;
                person.SelfNow = c.Snapshot.SelfConcept;
                foreach (var edge in c.Snapshot.Relationships.Edges.Values)
                    person.Closeness[edge.B] = edge.Closeness;
                person.Stress.Add((day, c.Snapshot.Psychology.Stress));
            }
        }

        private static Sample Take(int day, WDateTime now, IReadOnlyList<IHuman> characters)
        {
            var drift = characters.Where(h => h.Snapshot.Interests is not null).Select(h => h.Snapshot.Interests!)
                .Select(s => new[]
                {
                    s.Current.Realistic - s.Baseline.Realistic, s.Current.Investigative - s.Baseline.Investigative,
                    s.Current.Artistic - s.Baseline.Artistic, s.Current.Social - s.Baseline.Social,
                    s.Current.Enterprising - s.Baseline.Enterprising, s.Current.Conventional - s.Baseline.Conventional,
                }.Max(Math.Abs))
                .DefaultIfEmpty(0).Max();

            return new Sample(
                day,
                WWorld.Spec.ToClock24(now.Hour % WWorld.Spec.HoursPerDay),
                characters.Select(c => c.Snapshot.Psychology.Stress).OrderBy(x => x).ToList(),
                characters.Count(c => c.Snapshot.InteractionSurface.HasPrivacy),
                characters.Count(c => c.Identity.HomeLocationId == c.Snapshot.InteractionSurface.Location),
                characters.Count(c => c.Snapshot.Behavior.ActiveSleep is not null),
                characters.Average(c => c.Snapshot.Psychology.Motivations?.NeedSocial ?? 0),
                characters.Average(c => c.Snapshot.Physiology.Energy),
                drift);
        }
    }
}
