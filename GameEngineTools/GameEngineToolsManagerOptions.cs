// GameEngineToolsManagerOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System;
    using GameEngineTools.World.Core.Time;

    public sealed class GameEngineToolsManagerOptions
    {
        public required Func<WorldClock> InitializeWorldClock { get; set; }
        public string LogsRoot { get; set; } = "logs";
        public bool UseConsoleLogging { get; set; } = true;
    }
}
