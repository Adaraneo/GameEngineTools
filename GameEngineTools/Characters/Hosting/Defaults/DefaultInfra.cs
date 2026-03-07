// DefaultInfra.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Hosting.Defaults
{
    using System.Collections.Concurrent;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Logging;

    public interface IRandomSourceFactory
    {
        IRandomSource Create(int seed);
    }

    internal sealed class RandomSourceFactory : IRandomSourceFactory
    {
        public IRandomSource Create(int seed) => new SeededRandom(seed);

        private sealed class SeededRandom : IRandomSource
        {
            private readonly Random _rng;
            public SeededRandom(int seed) => _rng = new Random(seed);
            public int Next(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);
            public double NextUnit() => _rng.NextDouble();
            public bool Chance(double p) => _rng.NextDouble() < p;
        }
    }

    internal sealed class InMemoryEventBus : IEventBus
    {
        private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
        private readonly ILogger _log;

        public InMemoryEventBus(ILogger<InMemoryEventBus> log)
        {
            _log = log;
        }

        public void Publish(IDomainEvent @event)
        {
            var t = @event.GetType();
            if (!_handlers.TryGetValue(t, out var list)) return;

            Delegate[] snapshot;
            lock (list) { snapshot = list.ToArray(); }

            foreach (var h in snapshot)
            {
                try
                {
                    h.DynamicInvoke(@event);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "EventBus handler failed for event {0}", t.Name);
                }
            }
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class, IDomainEvent
        {
            var list = _handlers.GetOrAdd(typeof(TEvent), _ => new List<Delegate>());
            lock (list) list.Add(handler);

            return new Unsubscriber(() =>
            {
                lock (list) list.Remove(handler);
            });
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly Action _dispose;
            public Unsubscriber(Action dispose) => _dispose = dispose;
            public void Dispose() => _dispose();
        }
    }

    internal sealed class SimpleScheduler : IScheduler
    {
        private readonly object _lock = new();
        private readonly List<ScheduledItem> _items = new();

        public ScheduledId ScheduleAt(WDateTime when, ScheduledAction action, string? tag = null)
        {
            var id = new ScheduledId(Guid.NewGuid());
            lock (_lock) _items.Add(new ScheduledItem(id, when, action, tag));
            return id;
        }

        public ScheduledId ScheduleAfter(WDateTime now, WTimeSpan delay, ScheduledAction action, string? tag = null)
        {
            if (now == default)
                throw new ArgumentException("'now' must not be defaul(WDateTime).", nameof(now));

            return ScheduleAt(now + delay, action, tag);
        }

        public bool Cancel(ScheduledId id)
        {
            lock (_lock)
            {
                var idx = _items.FindIndex(i => i.Id.Equals(id));
                if (idx >= 0) { _items.RemoveAt(idx); return true; }
                return false;
            }
        }

        public IEnumerable<(ScheduledId id, ScheduledAction action)> Due(WDateTime now)
        {
            List<ScheduledItem> due;
            lock (_lock)
            {
                due = _items.Where(i => Compare(i.When, now) <= 0).ToList();
                foreach (var d in due) _items.Remove(d);
            }
            foreach (var d in due)
                yield return (d.Id, d.Action);
        }

        private static int Compare(WDateTime a, WDateTime b)
        {
            return Comparer<WDateTime>.Default.Compare(a, b);
        }

        private sealed record ScheduledItem(ScheduledId Id, WDateTime When, ScheduledAction Action, string? Tag);
    }
}
