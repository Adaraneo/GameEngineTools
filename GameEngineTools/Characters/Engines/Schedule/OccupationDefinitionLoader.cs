// OccupationDefinitionLoader.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Schedule
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Loads <see cref="OccupationDefinition"/> instances from a JSON file or string
    /// and registers them into an <see cref="IOccupationRegistry"/>.
    /// </summary>
    /// <remarks>
    /// Expected JSON format:
    /// <code>
    /// [
    ///   {
    ///     "id": "innkeeper",
    ///     "slots": [
    ///       {
    ///         "slotId": "innkeeper_prep_morning",
    ///         "hourOfDay": 7,
    ///         "action": "SelfCare",
    ///         "locationKey": null,
    ///         "biasStrength": 0.6,
    ///         "canSkipWhenStressed": false
    ///       }
    ///     ]
    ///   }
    /// ]
    /// </code>
    /// </remarks>
    public static class OccupationDefinitionLoader
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>
        /// Loads and registers occupation definitions from a JSON file.
        /// </summary>
        /// <param name="path">Absolute or relative path to the JSON file.</param>
        /// <param name="registry">Target registry.</param>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="JsonException">Thrown when the JSON is malformed.</exception>
        public static void LoadFromFile(string path, IOccupationRegistry registry)
        {
            var json = File.ReadAllText(path);
            LoadFromJson(json, registry);
        }

        /// <summary>
        /// Loads and registers occupation definitions from a JSON string.
        /// </summary>
        /// <param name="json">JSON string containing an array of occupation definitions.</param>
        /// <param name="registry">Target registry.</param>
        /// <exception cref="JsonException">Thrown when the JSON is malformed.</exception>
        public static void LoadFromJson(string json, IOccupationRegistry registry)
        {
            var dtos = JsonSerializer.Deserialize<List<OccupationDto>>(json, _options)
                       ?? throw new JsonException("Occupation JSON must be a non-null array.");

            foreach (var dto in dtos)
            {
                if (string.IsNullOrWhiteSpace(dto.Id))
                    throw new JsonException("Each occupation must have a non-empty 'id' field.");

                var slots = new List<ScheduleSlotTemplate>(dto.Slots?.Count ?? 0);
                foreach (var s in dto.Slots ?? new List<SlotDto>())
                {
                    slots.Add(new ScheduleSlotTemplate(
                        s.SlotId ?? throw new JsonException($"Slot in occupation '{dto.Id}' is missing 'slotId'."),
                        s.HourOfDay,
                        s.Action ?? throw new JsonException($"Slot '{s.SlotId}' is missing 'action'."),
                        s.LocationKey,
                        s.BiasStrength,
                        s.CanSkipWhenStressed));
                }

                registry.Register(new OccupationDefinition(dto.Id, slots));
            }
        }

        // ── CSV loading ───────────────────────────────────────────────────────

        /// <summary>
        /// Loads and registers occupation definitions from a semicolon-delimited CSV file.
        /// </summary>
        /// <remarks>
        /// Expected format — one row per slot, occupation ID repeated across rows:
        /// <code>
        /// OccupationId;SlotId;HourOfDay;Action;LocationKey;BiasStrength;CanSkipWhenStressed
        /// innkeeper;innkeeper_prep;7;SelfCare;;0.6;false
        /// innkeeper;innkeeper_work;10;Work;;0.8;true
        /// </code>
        /// The first row is always treated as a header and skipped.
        /// Empty <c>LocationKey</c> is stored as <c>null</c>.
        /// Lines starting with <c>#</c> are treated as comments and skipped.
        /// </remarks>
        /// <param name="path">Path to the CSV file.</param>
        /// <param name="registry">Target registry.</param>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="FormatException">Thrown when a row cannot be parsed.</exception>
        public static void LoadFromCsv(string path, IOccupationRegistry registry)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            reader.ReadLine(); // skip header

            // Accumulate slots grouped by occupation ID
            var groups = new Dictionary<string, List<ScheduleSlotTemplate>>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>(); // preserve declaration order

            string? line;
            var lineNumber = 1;
            while ((line = reader.ReadLine()) != null)
            {
                lineNumber++;
                line = line.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                    continue;

                var cols = line.Split(';');
                if (cols.Length < 7)
                    throw new FormatException(
                        $"Occupations.csv line {lineNumber}: expected 7 columns, got {cols.Length}.");

                var occupationId = cols[0].Trim();
                var slotId = cols[1].Trim();
                var hourOfDay = int.Parse(cols[2].Trim());
                var action = cols[3].Trim();
                var locationKey = string.IsNullOrEmpty(cols[4].Trim()) ? null : cols[4].Trim();
                var biasStrength = double.Parse(cols[5].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                var canSkip = bool.Parse(cols[6].Trim());

                if (!groups.ContainsKey(occupationId))
                {
                    groups[occupationId] = new List<ScheduleSlotTemplate>();
                    order.Add(occupationId);
                }

                groups[occupationId].Add(new ScheduleSlotTemplate(slotId, hourOfDay, action, locationKey, biasStrength, canSkip));
            }

            foreach (var id in order)
            {
                registry.Register(new OccupationDefinition(id, groups[id]));
            }
        }

        // ── Private DTOs ──────────────────────────────────────────────────────

        private sealed class OccupationDto
        {
            public string? Id { get; init; }
            public List<SlotDto>? Slots { get; init; }
        }

        private sealed class SlotDto
        {
            public string? SlotId { get; init; }
            public int HourOfDay { get; init; }
            public string? Action { get; init; }
            public string? LocationKey { get; init; }
            public double BiasStrength { get; init; } = 0.7;
            public bool CanSkipWhenStressed { get; init; } = true;
        }
    }
}
