// CharactersJsonLogFormatter.cs
// Copyright (c) 50PSoftware

using System.Text.Json;

namespace GameEngineTools.Logging
{
    /// <summary>
    /// Serialises character log entries into a compact JSON Lines form.
    /// </summary>
    internal static class CharactersJsonLogFormatter
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false
        };

        public static string Format(CharactersLogEntry entry)
            => JsonSerializer.Serialize(entry, JsonOptions);
    }
}
