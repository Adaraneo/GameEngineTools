// Collector.cs
// Copyright (c) 50PSoftware

namespace WorldMonitor
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Behavior;
    using GameEngineTools.Characters.Engines.Bereavement;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.Characters.Engines.Physiology;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>The stress of everyone at one moment of the run.</summary>
    public sealed record Sample(int Day, double ClockHour24, IReadOnlyList<double> Stress, int Private, int AtHome, int Asleep, double NeedSocial, double Energy, double InterestDrift);

    /// <summary>Per-world-day counters taken at every tick.</summary>
    public sealed class DayCounters
    {
        public long Total, Private, AtHome, Asleep, AsleepPrivate;
    }

    /// <summary>Gathers the measurements of a run; <see cref="Record"/> is called once per scene tick.</summary>
    public sealed class Collector
    {
        private readonly double _sampleEveryHours;
        private WDateTime? _start;
        private long _lastSampleSlot = -1;
        private readonly Dictionary<HumanId, bool> _wasAsleep = new();
        private readonly HashSet<(long, HumanId, HumanId)> _seenOutcomes = new();
        private readonly HashSet<(long, HumanId, Type)> _seenOther = new();

        public Collector(double sampleEveryHours) => _sampleEveryHours = sampleEveryHours;

        public List<Sample> Samples { get; } = new();
        public SortedDictionary<int, DayCounters> Days { get; } = new();
        public Dictionary<string, long> LocationTicks { get; } = new();
        public Dictionary<string, int> SleepStarts { get; } = new();
        public Dictionary<string, long> Actions { get; } = new();
        public Dictionary<string, long> Events { get; } = new();
        public int InteractionsAccepted { get; private set; }
        public int InteractionsRejected { get; private set; }
        public int SexualEncounters { get; private set; }
        public int Pregnancies { get; private set; }
        public int Births { get; private set; }
        public int Deaths { get; private set; }
        public long Ticks { get; private set; }
        public int CharactersSeen { get; private set; }

        public void Record(WDateTime now, IReadOnlyList<IHuman> characters)
        {
            _start ??= now;
            Ticks++;
            CharactersSeen = Math.Max(CharactersSeen, characters.Count);

            var hours = WDateTime.Difference(now, _start.Value).TotalHours;
            var day = (int)(hours / WWorld.Spec.HoursPerDay);
            if (!Days.TryGetValue(day, out var counters))
                Days[day] = counters = new DayCounters();

            foreach (var c in characters)
            {
                var surface = c.Snapshot.InteractionSurface;
                var location = surface.Location ?? "?";
                var asleep = c.Snapshot.Behavior.ActiveSleep is not null;
                var atHome = c.Identity.HomeLocationId == location;

                counters.Total++;
                if (surface.HasPrivacy) counters.Private++;
                if (atHome) counters.AtHome++;
                if (asleep) { counters.Asleep++; if (surface.HasPrivacy) counters.AsleepPrivate++; }
                LocationTicks[location] = LocationTicks.GetValueOrDefault(location) + 1;

                NoteSleepStart(c, asleep, atHome, surface.HasPrivacy);
                CountEvents(c);
            }

            var slot = (long)(hours / _sampleEveryHours);
            if (slot != _lastSampleSlot && characters.Count > 0)
            {
                _lastSampleSlot = slot;
                Samples.Add(Take(day, now, characters));
            }
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
                        break;
                    case InteractionOutcome io when _seenOutcomes.Add((io.OccurredAt.WorldTicks, io.From, io.To)):
                        if (io.Accepted) InteractionsAccepted++; else InteractionsRejected++;
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
                        break;
                }
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
