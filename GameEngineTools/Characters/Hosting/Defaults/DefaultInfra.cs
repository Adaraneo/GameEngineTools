// DefaultInfra.cs
// Copyright (c) 50PSoftware

using System.Collections.Concurrent;
using GameEngineTools.Characters.Core;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Logging;

namespace GameEngineTools.Characters.Hosting.Defaults;

// ---------- RNG ----------

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

// ---------- EventBus ----------

internal sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Publish(IDomainEvent @event)
    {
        var t = @event.GetType();
        if (_handlers.TryGetValue(t, out var list))
        {
            // defensive copy – handlers se mohou měnit za běhu
            foreach (var h in list.ToArray())
            {
                try { h.DynamicInvoke(@event); }
                catch { /* swallowing; OrchestratedHuman loguje na své straně */ }
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

// ---------- Scheduler ----------

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

    public ScheduledId ScheduleAfter(WTimeSpan delay, ScheduledAction action, string? tag = null)
    {
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
            due = _items.Where(i => i.When.Equals(default(WDateTime)) || Compare(i.When, now) <= 0).ToList();
            foreach (var d in due) _items.Remove(d);
        }
        foreach (var d in due)
            yield return (d.Id, d.Action);
    }

    private static int Compare(WDateTime a, WDateTime b)
    {
        // Předpoklad: WDateTime má porovnatelnost; když ne, nahraď vlastní logikou.
        return Comparer<WDateTime>.Default.Compare(a, b);
    }

    private sealed record ScheduledItem(ScheduledId Id, WDateTime When, ScheduledAction Action, string? Tag);
}

