// GameEngineToolsManagerOptions.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    /// <summary>Startup options for <see cref="IGameEngineToolsManager"/>.</summary>
    public sealed class GameEngineToolsManagerOptions
    {
        /// <summary>Root directory for log output.</summary>
        public string LogsRoot { get; set; } = "logs";

        /// <summary>Whether to enable console logging.</summary>
        public bool UseConsoleLogging { get; set; } = true;
    }
}
