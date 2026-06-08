// MemoryWhatParser.cs
// Copyright (c) 50PSoftware
//
// Helper parser for the What schema in EpisodicMemory.
//
// Schema format: {Category}:{Type}:{Outcome}|{key}={value}|{key}={value}
// Example:       Interaction:SmallTalk:Accepted|from=a3f2c1d0|to=b7e9a2f1
//
// Pravidla schematu:
//   · The header (before the first |) is always present — Category:Type or Category:Type:Outcome
//   · Parameters (after |) are optional — key=value pairs separated by |
//   · HumanId is shortened to the first 8 chars of the N-format (no dashes) — readable, unique for logs
//   · What is a deterministic key for reinforcement in Encode() — MUST NOT contain a timestamp

namespace GameEngineTools.Characters.Engines.Memory
{
    using System;

    /// <summary>
    /// Static helper class for building and parsing the <c>What</c> string
    /// v <see cref="EpisodicMemory"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a separate class rather than methods directly in the engine?</b><br/>
    /// SRP — the parser is responsible for the format, the engine for the encoding logic.
    /// It also makes the parser testable in isolation without depending on the whole engine.
    /// </para>
    /// <para>
    /// <b>Dual purpose of the <c>What</c> string:</b>
    /// <list type="number">
    ///   <item>
    ///     <b>Reinforcement key</b> — <see cref="DefaultMemoryEngine.Encode"/> looks for an existing
    ///     episode with the same <c>What</c>. If it finds one, it reinforces it instead of creating a new one.
    ///     Therefore <c>What</c> must be deterministic for the same event type with the same actors.
    ///   </item>
    ///   <item>
    ///     <b>Narrative source</b> — <c>DefaultNarrativeFormatter</c> parses the parameters
    ///     and translates them into a readable sentence. HumanId is resolved to a name via the resolver.
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    internal static class MemoryWhatParser
    {
        // ══════════════════════════════════════════════════════════════════════════
        // Building the What string
        // ══════════════════════════════════════════════════════════════════════════

        #region Build — sestavení What

        /// <summary>
        /// Builds the <c>What</c> for an interaction (a SpeechAct between two characters).
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Interaction:{Act}:{Accepted|Rejected}|from={id}|to={id}</c>
        /// </remarks>
        /// <param name="act">The speech-act type (SmallTalk, Humor…).</param>
        /// <param name="accepted">Whether the interaction was accepted.</param>
        /// <param name="from">Initiator id.</param>
        /// <param name="to">Recipient id.</param>
        public static string Interaction(string act, bool accepted, Guid from, Guid to)
            => $"Interaction:{act}:{(accepted ? "Accepted" : "Rejected")}" +
               $"|from={Full(from)}|to={Full(to)}";

        /// <summary>
        /// Builds the <c>What</c> for a performed action (the character's own action).
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Action:{ActionName}</c><br/>
        /// No parameters — the character remembers its own action, not other actors.
        /// </remarks>
        /// <param name="actionName">Action name from <see cref="ActionNames"/>.</param>
        public static string Action(string actionName)
            => $"Action:{actionName}";

        /// <summary>
        /// Builds the <c>What</c> for the end of sleep.
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Sleep:Ended:{High|Medium|Low|Poor}|hours={h:F1}</c><br/>
        /// Sleep quality is discretized — not an exact number (which would prevent reinforcement).
        /// </remarks>
        /// <param name="quality">Sleep quality 0–100.</param>
        /// <param name="totalHours">Sleep duration in hours.</param>
        public static string SleepEnded(double quality, double totalHours)
        {
            // Quality discretization — an exact number would prevent reinforcement
            // (slightly different quality each night → the same What would never recur)
            var qualityBucket = quality switch
            {
                >= 80 => "High",
                >= 55 => "Medium",
                >= 30 => "Low",
                _ => "Poor"
            };
            return $"Sleep:Ended:{qualityBucket}|hours={totalHours:F1}";
        }

        /// <summary>
        /// Builds the <c>What</c> for a nightmare.
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Sleep:Nightmare|stress={High|Medium|Low}</c>
        /// </remarks>
        /// <param name="stressAtSleep">Stress level at the moment of falling asleep (0–100).</param>
        public static string Nightmare(double stressAtSleep)
        {
            var stressBucket = stressAtSleep switch
            {
                >= 70 => "High",
                >= 40 => "Medium",
                _ => "Low"
            };
            return $"Sleep:Nightmare|stress={stressBucket}";
        }

        /// <summary>
        /// Builds the <c>What</c> for a first impression of a new character.
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Relation:FirstImpression:{Positive|Neutral|Negative}|of={id}</c>
        /// </remarks>
        /// <param name="like">Hodnota Like z <see cref="GameEngineTools.Characters.Engines.Relationships.FirstImpressionFormed"/> (0–100).</param>
        /// <param name="of">ID postavy, na kterou dojem vznikl.</param>
        public static string FirstImpression(double like, Guid of)
        {
            var sentiment = like switch
            {
                >= 70 => "Positive",
                >= 45 => "Neutral",
                _ => "Negative"
            };
            return $"Relation:FirstImpression:{sentiment}|of={Full(of)}";
        }

        /// <summary>
        /// Builds the <c>What</c> for a repair attempt.
        /// </summary>
        /// <remarks>
        /// Resulting format: <c>Relation:Repair:{Accepted|Rejected}|with={id}</c>
        /// </remarks>
        public static string RepairAttempt(bool accepted, Guid with)
            => $"Relation:Repair:{(accepted ? "Accepted" : "Rejected")}|with={Full(with)}";

        #endregion Build — sestavení What

        // ══════════════════════════════════════════════════════════════════════════
        // Parsing the What string
        // ══════════════════════════════════════════════════════════════════════════

        #region Parse — čtení What

        /// <summary>
        /// Returns the header of the <c>What</c> string — the part before the first <c>|</c>.
        /// </summary>
        /// <param name="what">The complete <c>What</c> string.</param>
        /// <returns>The header, e.g. <c>"Interaction:SmallTalk:Accepted"</c>.</returns>
        public static string GetHeader(string what)
        {
            var idx = what.IndexOf('|');
            return idx < 0 ? what : what[..idx];
        }

        /// <summary>
        /// Returns the value of a named parameter from the <c>What</c> string.
        /// </summary>
        /// <param name="what">The complete <c>What</c> string.</param>
        /// <param name="key">Parameter name (e.g. <c>"from"</c>, <c>"to"</c>).</param>
        /// <returns>Hodnota parametru, nebo <c>null</c> pokud parametr neexistuje.</returns>
        /// <example>
        /// <code>
        /// var what = "Interaction:SmallTalk:Accepted|from=a3f2c1d0|to=b7e9a2f1";
        /// var from = MemoryWhatParser.GetParam(what, "from"); // → "a3f2c1d0"
        /// var to   = MemoryWhatParser.GetParam(what, "to");   // → "b7e9a2f1"
        /// </code>
        /// </example>
        public static string? GetParam(string what, string key)
        {
            // Skip the header (the part before the first |) and iterate over the parameters
            var parts = what.AsSpan();
            var firstPipe = parts.IndexOf('|');
            if (firstPipe < 0) return null;

            var remaining = parts[(firstPipe + 1)..];

            while (remaining.Length > 0)
            {
                // Find the next | or the end of the string
                var next = remaining.IndexOf('|');
                var segment = next < 0 ? remaining : remaining[..next];

                // Split into key=value
                var eq = segment.IndexOf('=');
                if (eq > 0)
                {
                    var segKey = segment[..eq];
                    if (segKey.SequenceEqual(key.AsSpan()))
                        return segment[(eq + 1)..].ToString();
                }

                if (next < 0) break;
                remaining = remaining[(next + 1)..];
            }

            return null;
        }

        #endregion Parse — čtení What

        #region Percieved Memory

        internal enum PerceivedMemoryTone
        {
            Neutral = 0,
            Warm = 1,
            Threat = 2,
            Slight = 3
        }

        internal sealed record MemoryEpisodeDescriptor(
            string Category,
            string Type,
            string? Outcome,
            IReadOnlyDictionary<string, string> Parameters,
            PerceivedMemoryTone PerceivedTone);

        /// <summary>
        /// Parses <c>What</c> and <c>PerceivedWhat</c> into a structured descriptor,
        /// so semantic inference can be built on it without fragile <c>Contains(...)</c> calls.
        /// </summary>
        public static MemoryEpisodeDescriptor ParseDescriptor(string? what, string? perceivedWhat)
        {
            var raw = what ?? string.Empty;
            var header = GetHeader(raw);

            var parts = header.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var category = parts.Length > 0 ? parts[0] : string.Empty;
            var type = parts.Length > 1 ? parts[1] : string.Empty;
            var outcome = parts.Length > 2 ? parts[2] : null;

            var parameters = ParseParams(raw);

            var tone = PerceivedMemoryTone.Neutral;
            if (!string.IsNullOrWhiteSpace(perceivedWhat))
            {
                if (perceivedWhat.StartsWith("PerceivedThreat:", StringComparison.Ordinal))
                {
                    tone = PerceivedMemoryTone.Threat;
                }
                else if (perceivedWhat.StartsWith("PerceivedWarmth:", StringComparison.Ordinal))
                {
                    tone = PerceivedMemoryTone.Warm;
                }
                else if (perceivedWhat.StartsWith("PerceivedSlight:", StringComparison.Ordinal))
                {
                    tone = PerceivedMemoryTone.Slight;
                }
            }

            return new MemoryEpisodeDescriptor(category, type, outcome, parameters, tone);
        }

        /// <summary>
        /// Returns the canonical micro-event token from the <c>what</c> parameter.
        /// If the parameter is absent or empty, returns <see langword="null" />.
        /// </summary>
        public static string? GetMicroEventKind(MemoryEpisodeDescriptor descriptor)
        {
            if (!descriptor.Parameters.TryGetValue("what", out var value))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Parses the part after the header into a map of <c>key=value</c> parameters.
        /// </summary>
        private static IReadOnlyDictionary<string, string> ParseParams(string what)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var idx = what.IndexOf('|');
            if (idx < 0 || idx >= what.Length - 1)
            {
                return result;
            }

            var tail = what[(idx + 1)..];
            foreach (var segment in tail.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = segment.IndexOf('=');
                if (eq <= 0 || eq >= segment.Length - 1)
                {
                    continue;
                }

                var key = segment[..eq];
                var value = segment[(eq + 1)..];
                result[key] = value;
            }

            return result;
        }

        #endregion Percieved Memory

        // ══════════════════════════════════════════════════════════════════════════
        // Private helper methods
        // ══════════════════════════════════════════════════════════════════════════

        #region Privátní — Full ID

        /// <summary>
        /// Returns the Guid as a string.
        /// </summary>
        private static string Full(Guid id)
            => id.ToString("N");

        #endregion Privátní — Full ID
    }
}
