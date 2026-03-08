// GameEngineToolsManagerInitializer.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System.Threading.Tasks;

    using Microsoft.Extensions.Hosting;
    public sealed class GameEngineToolsManagerInitializer : IHostedService
    {
        private readonly IGameEngineToolsManager _cm;
        public GameEngineToolsManagerInitializer(IGameEngineToolsManager cm) => _cm = cm;

        public Task StartAsync(CancellationToken ct)
        {
            _cm.Initialize();
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
