// CsvWorldObjectProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Constants;
    using GameEngineTools.FileSystem;

    /// <summary>
    /// CSV-backed implementation of <see cref="IWorldObjectProvider"/> with lazy per-location loading.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each location has its own file: <c>SourceFiles\World\Objects\{locationId}.csv</c>.
    /// The file is read from disk only on the first call to <see cref="GetObjectsAt"/>
    /// for that location. Subsequent calls return the cached result.
    /// </para>
    /// <para>
    /// <b>This is the Phase 1 implementation.</b> In Phase 2 (Unity integration),
    /// replace this with <c>UnityWorldObjectProvider : IWorldObjectProvider</c>
    /// without touching any simulation engine code.
    /// </para>
    /// <para>
    /// <b>Object file column order:</b><br/>
    /// <c>Id;DisplayName;Category;HeatSignature;AmbientNoise;BlocksLineOfSight;IsAvailable;Affordances</c>
    /// </para>
    /// <para>
    /// Affordances are pipe-separated key:value pairs, e.g.:<br/>
    /// <c>Warmth:0.7|Social:0.3|MoodBoost:0.2</c>
    /// </para>
    /// </remarks>
    public sealed class CsvWorldObjectProvider : IWorldObjectProvider
    {
        #region Private state

        /// <summary>
        /// Directory that contains per-location CSV files (one file per location ID).
        /// </summary>
        private readonly string _objectsDirectory;

        /// <summary>
        /// Per-location object cache. Populated lazily on first access.
        /// Uses <see cref="ConcurrentDictionary{TKey,TValue}"/> to be safe
        /// if multiple threads ever access the provider simultaneously.
        /// Stored as <see cref="List{T}"/> internally so items can be removed at runtime.
        /// </summary>
        private readonly ConcurrentDictionary<string, List<WorldObject>> _cache = new();

        #endregion

        #region Construction

        /// <summary>
        /// Initialises the provider pointing at the default objects directory
        /// from <see cref="FileSystemConstant.SourceFilePath.WorldObjectsDirectory"/>.
        /// </summary>
        public CsvWorldObjectProvider()
            : this(FileSystemConstant.SourceFilePath.WorldObjectsDirectory) { }

        /// <summary>
        /// Initialises the provider with an explicit directory path.
        /// Intended for tests and alternative configurations.
        /// </summary>
        /// <param name="objectsDirectory">
        /// Path to the directory containing per-location CSV files.
        /// </param>
        public CsvWorldObjectProvider(string objectsDirectory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(objectsDirectory);
            _objectsDirectory = objectsDirectory;
        }

        #endregion

        #region IWorldObjectProvider

        /// <inheritdoc/>
        /// <remarks>
        /// Loads <c>{objectsDirectory}\{locationId}.csv</c> on first call;
        /// returns cached result on all subsequent calls.
        /// Returns an empty list if the file does not exist (location has no objects).
        /// </remarks>
        public IEnumerable<WorldObject> GetObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);

            // GetOrAdd is atomic — the factory will only run once per locationId.
            return _cache.GetOrAdd(locationId, LoadForLocation)
                         .Where(o => o.HeldBy is null && o.ConsumedAt is null);
        }

        /// <summary>
        /// Removes an object from the cached list for the specified location.
        /// Call this when a character picks up an item to make it unavailable to others.
        /// </summary>
        /// <param name="locationId">Location where the object is present.</param>
        /// <param name="objectId">ID of the object to remove.</param>
        /// <returns><c>true</c> if the object was found and removed; <c>false</c> otherwise.</returns>
        public bool RemoveObject(string locationId, string objectId)
        {
            if (!_cache.TryGetValue(locationId, out var list))
                return false;
            return list.RemoveAll(o => o.Id == objectId) > 0;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Loads ALL per-location CSV files from the objects directory.
        /// Results are cached, so subsequent calls to <see cref="GetObjectsAt"/>
        /// for already-loaded locations are free.
        /// </remarks>
        public IEnumerable<WorldObject> GetAllObjects()
        {
            if (!Directory.Exists(_objectsDirectory))
                return Enumerable.Empty<WorldObject>();

            // Load every CSV file found in the directory, then flatten.
            return Directory
                .GetFiles(_objectsDirectory, $"*{FileSystemConstant.Extension.SourceCsv}")
                .SelectMany(filePath =>
                {
                    // Derive locationId from filename (e.g. "castle_hall.csv" → "castle_hall").
                    var locationId = Path.GetFileNameWithoutExtension(filePath);
                    return (IReadOnlyList<WorldObject>)_cache.GetOrAdd(locationId, _ => LoadFromPath(filePath, locationId));
                });
        }

        /// <summary>
        /// Returns all objects at <paramref name="locationId"/>, including those that are
        /// currently held or consumed — unlike <see cref="GetObjectsAt"/> which filters those out.
        /// </summary>
        public IEnumerable<WorldObject> GetAllObjectsAt(string locationId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
            return _cache.GetOrAdd(locationId, LoadForLocation);
        }

        /// <summary>
        /// Returns all location IDs that have been loaded into the cache.
        /// Used by <see cref="ObjectRespawnScheduler"/> to iterate all known locations.
        /// </summary>
        public IEnumerable<string> GetKnownLocationIds()
            => _cache.Keys;

        /// <summary>
        /// Sets or clears the holder of an object. Pass <c>null</c> to release the object.
        /// </summary>
        public bool SetHeldBy(string locationId, string objectId, Characters.Core.HumanId? holder)
        {
            if (!_cache.TryGetValue(locationId, out var list))
                return false;
            var idx = list.FindIndex(o => o.Id == objectId);
            if (idx < 0) return false;
            list[idx] = list[idx] with { HeldBy = holder };
            return true;
        }

        /// <summary>
        /// Marks an object as consumed at the given simulation time.
        /// The object is excluded from <see cref="GetObjectsAt"/> until respawned.
        /// </summary>
        public bool ConsumeObject(string locationId, string objectId, World.Utils.Time.WDateTime now)
        {
            if (!_cache.TryGetValue(locationId, out var list))
                return false;
            var idx = list.FindIndex(o => o.Id == objectId);
            if (idx < 0) return false;
            list[idx] = list[idx] with { ConsumedAt = now };
            return true;
        }

        /// <summary>
        /// Restores a held/consumed object to its available state.
        /// </summary>
        public bool RestoreObject(string locationId, string objectId)
        {
            if (!_cache.TryGetValue(locationId, out var list))
                return false;
            var idx = list.FindIndex(o => o.Id == objectId);
            if (idx < 0) return false;
            list[idx] = list[idx] with { HeldBy = null, ConsumedAt = null };
            return true;
        }

        /// <inheritdoc/>
        public void AddObject(WorldObject obj)
        {
            var list = _cache.GetOrAdd(obj.LocationId, _ => new List<WorldObject>());
            list.Add(obj);
        }

        /// <summary>
        /// Finds a world object by ID across all currently cached locations.
        /// Returns <c>null</c> if the object is not in the cache.
        /// </summary>
        public WorldObject? FindObject(string objectId)
        {
            foreach (var list in _cache.Values)
            {
                var obj = list.FirstOrDefault(o => o.Id == objectId);
                if (obj is not null) return obj;
            }
            return null;
        }

        /// <summary>
        /// Returns all objects across all cached locations that are currently held by the specified character.
        /// </summary>
        public IEnumerable<WorldObject> GetHeldBy(Characters.Core.HumanId holder)
        {
            foreach (var list in _cache.Values)
                foreach (var obj in list.Where(o => o.HeldBy == holder))
                    yield return obj;
        }

        #endregion

        #region Private loading

        /// <summary>
        /// Lazy-load factory for <see cref="_cache"/>.
        /// Constructs the expected file path from <paramref name="locationId"/> and loads it.
        /// </summary>
        /// <param name="locationId">Location ID (used as filename without extension).</param>
        private List<WorldObject> LoadForLocation(string locationId)
        {
            var path = Path.Combine(_objectsDirectory, locationId + FileSystemConstant.Extension.SourceCsv);
            return LoadFromPath(path, locationId);
        }

        /// <summary>
        /// Reads and parses a single per-location CSV file.
        /// Returns an empty list if the file does not exist.
        /// </summary>
        /// <param name="filePath">Full path to the CSV file.</param>
        /// <param name="locationId">Location ID injected into every parsed object.</param>
        private static List<WorldObject> LoadFromPath(string filePath, string locationId)
        {
            if (!File.Exists(filePath))
                return new List<WorldObject>();

            return CsvLoader.Load(filePath, v => ParseObjectRow(v, locationId));
        }

        #endregion

        #region CSV parsing

        /// <summary>
        /// Parses a single row from a per-location objects CSV file.
        /// </summary>
        /// <remarks>
        /// Expected column order:
        /// 0=Id, 1=DisplayName, 2=Category, 3=HeatSignature,
        /// 4=AmbientNoise, 5=BlocksLineOfSight, 6=IsAvailable, 7=Affordances
        /// </remarks>
        /// <param name="v">Column values split by the CSV delimiter.</param>
        /// <param name="locationId">Location ID injected from the file name.</param>
        private static WorldObject ParseObjectRow(string[] v, string locationId)
            => new()
            {
                Id               = v[0].Trim(),
                DisplayName      = v[1].Trim(),
                Category         = Enum.Parse<WorldObjectCategory>(v[2].Trim(), ignoreCase: true),
                LocationId       = locationId,
                HeatSignature    = double.Parse(v[3].Trim(), CultureInfo.InvariantCulture),
                AmbientNoise     = double.Parse(v[4].Trim(), CultureInfo.InvariantCulture),
                BlocksLineOfSight = bool.Parse(v[5].Trim()),
                IsAvailable      = bool.Parse(v[6].Trim()),
                Affordances      = ParseAffordances(v[7].Trim()),
                IsPickable       = v.Length > 8  ? bool.Parse(v[8].Trim()) : false,
                WeightGrams      = v.Length > 9  ? int.Parse(v[9].Trim(), CultureInfo.InvariantCulture) : 0,
                ItemKind         = v.Length > 10 ? Enum.Parse<PickupItemKind>(v[10].Trim(), ignoreCase: true) : PickupItemKind.None,
                Respawns         = v.Length > 11 ? bool.Parse(v[11].Trim()) : false,
                RespawnMinutes   = v.Length > 12 ? int.Parse(v[12].Trim(), CultureInfo.InvariantCulture) : 1440
            };

        /// <summary>
        /// Parses the pipe-separated affordance string into an immutable array.
        /// </summary>
        /// <example>
        /// Input: <c>"Warmth:0.7|Social:0.3|MoodBoost:0.2"</c><br/>
        /// Output: three <see cref="WorldObjectAffordance"/> records.
        /// </example>
        /// <param name="raw">Raw affordance string from the CSV cell.</param>
        private static ImmutableArray<WorldObjectAffordance> ParseAffordances(string raw)
        {
            // An empty cell means the object has no affordances.
            if (string.IsNullOrWhiteSpace(raw))
                return ImmutableArray<WorldObjectAffordance>.Empty;

            var builder = ImmutableArray.CreateBuilder<WorldObjectAffordance>();

            foreach (var token in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');

                if (parts.Length != 2)
                    throw new FormatException(
                        $"Affordance token '{token}' is malformed. Expected format: 'Type:Value'.");

                var type         = Enum.Parse<AffordanceType>(parts[0].Trim(), ignoreCase: true);
                var satisfaction = double.Parse(parts[1].Trim(), CultureInfo.InvariantCulture);

                builder.Add(new WorldObjectAffordance(type, satisfaction));
            }

            return builder.ToImmutable();
        }

        #endregion
    }
}
