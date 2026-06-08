// CharactersFileLoggerOptions.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Configuration options for <see cref="CharactersFileLoggerProvider"/>.
    /// </summary>
    public sealed class CharactersFileLoggerOptions
    {
        /// <summary>
        /// Root directory for log files.
        /// Default value: <c>logs/Characters</c>.
        /// </summary>
        public string LogsDirectoryPath { get; set; } = Path.Combine("logs", "Characters");

        /// <summary>
        /// Minimum log level. Messages below this level are dropped.
        /// </summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// If <c>true</c>, real-time timestamps are in UTC; otherwise local time.
        /// </summary>
        public bool UseUtcTimestamps { get; set; } = true;

        /// <summary>
        /// Mirroring mode into per-person/per-subsystem files.
        /// </summary>
        public CharactersLogMirrorMode MirrorMode { get; set; } = CharactersLogMirrorMode.GlobalAndScoped;

        /// <summary>
        /// Enables textual <c>.log</c> files.
        /// </summary>
        public bool WriteTextLogs { get; set; } = true;

        /// <summary>
        /// Zapne companion <c>.jsonl</c> soubory.
        /// </summary>
        public bool WriteJsonLines { get; set; } = true;

        /// <summary>
        /// Optional accessor for the current world-time text written to the logs.
        /// </summary>
        public Func<string>? WorldTimeTextAccessor { get; set; }

        /// <summary>
        /// Optional accessor for the numeric world tick (<see cref="World.Utils.Time.WDateTime.WorldTicks"/>).
        /// Used as the grouping key for the tick-store in the reader. When not set, a
        /// safe fallback is used (0 until <c>WWorld</c> is configured).
        /// </summary>
        public Func<long>? WorldTicksAccessor { get; set; }
    }
}
