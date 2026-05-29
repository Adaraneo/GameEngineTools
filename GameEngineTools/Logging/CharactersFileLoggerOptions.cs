// CharactersFileLoggerOptions.cs
// Copyright (c) 50PSoftware

using Microsoft.Extensions.Logging;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Konfigurační volby pro <see cref="CharactersFileLoggerProvider"/>.
    /// </summary>
    public sealed class CharactersFileLoggerOptions
    {
        /// <summary>
        /// Kořenový adresář pro log soubory.
        /// Výchozí hodnota: <c>logs/Characters</c>.
        /// </summary>
        public string LogsDirectoryPath { get; set; } = Path.Combine("logs", "Characters");

        /// <summary>
        /// Minimální úroveň logování. Zprávy pod touto úrovní jsou zahozeny.
        /// </summary>
        public LogLevel MinLevel { get; set; } = LogLevel.Information;

        /// <summary>
        /// Pokud <c>true</c>, real-time timestamps jsou v UTC. Jinak lokální čas.
        /// </summary>
        public bool UseUtcTimestamps { get; set; } = true;

        /// <summary>
        /// Režim mirroringu do per-person/per-subsystem souborů.
        /// </summary>
        public CharactersLogMirrorMode MirrorMode { get; set; } = CharactersLogMirrorMode.GlobalAndScoped;

        /// <summary>
        /// Zapne textové <c>.log</c> soubory.
        /// </summary>
        public bool WriteTextLogs { get; set; } = true;

        /// <summary>
        /// Zapne companion <c>.jsonl</c> soubory.
        /// </summary>
        public bool WriteJsonLines { get; set; } = true;

        /// <summary>
        /// Volitelný accessor pro aktuální world time text zapisovaný do logů.
        /// </summary>
        public Func<string>? WorldTimeTextAccessor { get; set; }

        /// <summary>
        /// Volitelný accessor pro numerický world tick (<see cref="World.Utils.Time.WDateTime.WorldTicks"/>).
        /// Slouží jako grouping klíč pro tick-store v readeru. Když není nastaven, použije se
        /// bezpečný fallback (0, dokud není <c>WWorld</c> nakonfigurován).
        /// </summary>
        public Func<long>? WorldTicksAccessor { get; set; }
    }
}
