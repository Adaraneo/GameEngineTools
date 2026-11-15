// WorldBaseline.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using GameEngineTools.World.Objects;
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// The world as it was generated, kept as a separate file next to the working <c>world.db</c> (<c>world.gen.db</c>): generation writes it and then makes the working copy from it,
    /// a reset makes the working copy from it again.
    /// </summary>
    public interface IWorldBaseline
    {
        /// <summary>Path of the generated-world file.</summary>
        string BaselinePath { get; }

        /// <summary>Whether the generated-world file exists.</summary>
        bool Exists { get; }

        /// <summary>Returns the working world to the generated one: buffered writes are dropped, the database content replaced, caches emptied, host hooks run.</summary>
        /// <exception cref="FileNotFoundException">There is no generated-world file.</exception>
        void Reset();
    }

    /// <summary>Path rules and the copying step shared by WorldGen and the hosts.</summary>
    public static class WorldBaselineFiles
    {
        /// <summary><c>dir/world.db</c> gives <c>dir/world.gen.db</c>.</summary>
        public static string PathFor(string worldDbPath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(worldDbPath);
            var dir = Path.GetDirectoryName(worldDbPath);
            var name = Path.GetFileNameWithoutExtension(worldDbPath) + ".gen" + Path.GetExtension(worldDbPath);
            return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
        }

        /// <summary>Makes the working world from the generated one (a consistent copy through the SQLite backup, so open WAL files are no problem); an existing working file is replaced.</summary>
        public static void Publish(string baselinePath, string worldDbPath)
        {
            using var target = new SqliteWorldDatabase(worldDbPath);
            target.ResetFrom(baselinePath);
        }
    }

    /// <summary>Resets a running world to its generated file.</summary>
    public sealed class SqliteWorldBaseline : IWorldBaseline
    {
        private readonly SqliteWorldDatabase _db;
        private readonly WorldObjectWriteBuffer? _buffer;
        private readonly WorldObjectSnapshotCache? _cache;
        private readonly List<Action> _hooks = new();

        /// <param name="db">The open working database.</param>
        /// <param name="baselinePath">The generated-world file.</param>
        /// <param name="buffer">The write buffer in front of the database, whose pending writes a reset drops.</param>
        /// <param name="cache">The object snapshot cache, emptied by a reset.</param>
        public SqliteWorldBaseline(SqliteWorldDatabase db, string baselinePath, WorldObjectWriteBuffer? buffer = null, WorldObjectSnapshotCache? cache = null)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentException.ThrowIfNullOrWhiteSpace(baselinePath);
            _db = db;
            BaselinePath = baselinePath;
            _buffer = buffer;
            _cache = cache;
        }

        /// <inheritdoc/>
        public string BaselinePath { get; }

        /// <inheritdoc/>
        public bool Exists => File.Exists(BaselinePath);

        /// <summary>Registers something the host keeps in memory about the world (price ledger, indexes) that must be reset together with it.</summary>
        public void AddResetHook(Action hook) => _hooks.Add(hook ?? throw new ArgumentNullException(nameof(hook)));

        /// <inheritdoc/>
        public void Reset()
        {
            if (!Exists)
                throw new FileNotFoundException("The generated world file does not exist; run worldgen first.", BaselinePath);

            _buffer?.Discard();
            _db.ResetFrom(BaselinePath);
            _cache?.Invalidate();
            foreach (var hook in _hooks)
                hook();
        }
    }
}
