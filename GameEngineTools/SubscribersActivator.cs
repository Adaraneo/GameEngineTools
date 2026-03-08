// SubscribersActivator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Hosting;

    public sealed class CommonSubscribersActivator : IHostedService
    {
        private readonly IEnumerable<object> _subscribers;

        public CommonSubscribersActivator(IEnumerable<object> subscribers)
        {
            _subscribers = subscribers;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // just to ensure they are created
            var _ = _subscribers.ToList();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class SubscribersActivator : IHostedService
    {
        private readonly IServiceProvider _sp;

        public SubscribersActivator(IServiceProvider sp) => _sp = sp;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            //TODO: subscribe like this: _ = _sp.GetRequiredService<ObservationsFromInteractionsSubscriber>();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
