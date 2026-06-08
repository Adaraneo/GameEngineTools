// EventCollector.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Core
{
    using System;
    using System.Collections.Generic;

    /// <summary>Default list-backed implementation of <see cref="IEventCollector"/>.</summary>
    public sealed class EventCollector : IEventCollector
    {
        private readonly List<IDomainEvent> _events = new();

        /// <inheritdoc/>
        public void Add(IDomainEvent e) => _events.Add(e);

        /// <inheritdoc/>
        public IReadOnlyList<IDomainEvent> Drain()
        {
            if (_events.Count == 0)
            {
                return Array.Empty<IDomainEvent>();
            }

            var copy = _events.ToArray();
            _events.Clear();
            return copy;
        }
    }
}
