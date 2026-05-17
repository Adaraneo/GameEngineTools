// AffordanceApplicationServiceTests.cs
// Copyright (c) 50PSoftware

using GameEngineTools.Characters.Core;
using GameEngineTools.Characters.Engines.Objects;
using GameEngineTools.World.Objects;
using GameEngineTools.World.Utils.Time;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace EngineTests;

[TestClass]
public class AffordanceApplicationServiceTests : TestBase
{
    private static readonly WDateTime Now = new WDateTime(10000);
    private static readonly HumanId Actor = new HumanId(Guid.NewGuid());

    /// <summary>
    /// A list-backed event collector for test assertions.
    /// </summary>
    private sealed class ListEventCollector : IEventCollector
    {
        private readonly List<IDomainEvent> _events = new();
        public IReadOnlyList<IDomainEvent> Events => _events;
        public void Add(IDomainEvent @event) => _events.Add(@event);
        public IReadOnlyList<IDomainEvent> Drain()
        {
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }
    }

    private static IHumanContext MinimalCtx(HumanId id) => new HumanContext
    {
        Id = id,
        Logger = NullLogger.Instance,
        EventBus = new NullEventBus(),
        Scheduler = new NullScheduler(),
        Random = new ZeroRandom()
    };

    [TestMethod]
    public void Apply_OwnershipAffordanceOnly_EmitsNoObjectAffordanceApplied()
    {
        var obj = new WorldObject
        {
            Id = "gem_01",
            DisplayName = "Gem",
            Category = WorldObjectCategory.Ambient,
            LocationId = "dungeon",
            Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Ownership, 0.8))
        };

        var collector = new ListEventCollector();
        AffordanceApplicationService.Apply(obj, MinimalCtx(Actor), collector, Now);

        Assert.AreEqual(0, collector.Events.Count,
            "Ownership affordance should not emit ObjectAffordanceApplied.");
    }

    [TestMethod]
    public void Apply_HungerAffordance_EmitsObjectAffordanceApplied()
    {
        var obj = new WorldObject
        {
            Id = "apple_01",
            DisplayName = "Apple",
            Category = WorldObjectCategory.Food,
            LocationId = "orchard",
            Affordances = ImmutableArray.Create(new WorldObjectAffordance(AffordanceType.Hunger, 0.6))
        };

        var collector = new ListEventCollector();
        AffordanceApplicationService.Apply(obj, MinimalCtx(Actor), collector, Now);

        Assert.AreEqual(1, collector.Events.Count);
        var ev = collector.Events[0] as ObjectAffordanceApplied;
        Assert.IsNotNull(ev);
        Assert.AreEqual(AffordanceType.Hunger, ev!.AffordanceType);
        Assert.AreEqual(0.6, ev.Satisfaction, 0.001);
        Assert.AreEqual("apple_01", ev.ObjectId);
    }

    [TestMethod]
    public void Apply_MixedAffordances_SkipsOwnershipEmitsRest()
    {
        var obj = new WorldObject
        {
            Id = "herb_01",
            DisplayName = "Herb",
            Category = WorldObjectCategory.Food,
            LocationId = "garden",
            Affordances = ImmutableArray.Create(
                new WorldObjectAffordance(AffordanceType.MoodBoost, 0.3),
                new WorldObjectAffordance(AffordanceType.Ownership, 0.6),
                new WorldObjectAffordance(AffordanceType.Hunger, 0.2))
        };

        var collector = new ListEventCollector();
        AffordanceApplicationService.Apply(obj, MinimalCtx(Actor), collector, Now);

        Assert.AreEqual(2, collector.Events.Count,
            "MoodBoost and Hunger should emit; Ownership should be skipped.");
        var types = collector.Events
            .OfType<ObjectAffordanceApplied>()
            .Select(e => e.AffordanceType)
            .ToHashSet();
        Assert.IsTrue(types.Contains(AffordanceType.MoodBoost));
        Assert.IsTrue(types.Contains(AffordanceType.Hunger));
        Assert.IsFalse(types.Contains(AffordanceType.Ownership));
    }
}
