// EventCollector.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;

    public sealed class EventCollector : IEventCollector
    {
        private readonly List<IDomainEvent> _events = new();
        public void Add(IDomainEvent e) => _events.Add(e);
        public IReadOnlyList<IDomainEvent> Drain()
        {
            if (_events.Count == 0) return Array.Empty<IDomainEvent>();
            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }
    }
}
