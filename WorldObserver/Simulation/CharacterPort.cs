// CharacterPort.cs
// Copyright (c) 50PSoftware

namespace WorldObserver.Simulation
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using WorldObserver.Dtos;

    /// <summary>
    /// Bridge between the HTTP endpoints (export / import) and the live simulation loop. The
    /// <see cref="WorldHostedService"/> owns the scene and configures this once the world is built;
    /// the endpoints only call <see cref="Export"/> and <see cref="QueueImport"/>.
    /// </summary>
    /// <remarks>
    /// Export reads (immutable-by-reference) live state and is safe to run on the request thread.
    /// Import must mutate the scene (placement, narrative names, AddCharacter), so it is NOT applied
    /// on the request thread — the raw files are queued and applied on the simulation thread via
    /// <see cref="DrainImports"/> (called from <c>OnTick</c>), exactly like a birth.
    /// </remarks>
    public sealed class CharacterPort
    {
        private Func<ExportBundleDto>? _export;
        private readonly ConcurrentQueue<CharacterFileDto> _pending = new();
        private volatile bool _replace;
        private long _pendingWorldTimeTicks; // 0 = no world-time to apply on the next drain

        /// <summary>True once the simulation has wired up the export source (world is running).</summary>
        public bool Ready => _export is not null;

        /// <summary>Called by the hosted service after the world is built to provide the export source.</summary>
        public void Configure(Func<ExportBundleDto> export) => _export = export;

        /// <summary>Serializes the current live characters + world time (request thread).</summary>
        public ExportBundleDto Export() => _export?.Invoke() ?? new ExportBundleDto(0, new List<CharacterFileDto>());

        /// <summary>
        /// Queues import files; returns how many were accepted into the queue. When
        /// <paramref name="replace"/> is true, the next drain first clears the existing world.
        /// A positive <paramref name="worldTimeTicks"/> requests restoring the world clock on drain.
        /// </summary>
        public int QueueImport(IEnumerable<CharacterFileDto> files, bool replace, long worldTimeTicks)
        {
            var n = 0;
            foreach (var f in files)
            {
                if (f is null || string.IsNullOrWhiteSpace(f.Json)) continue;
                _pending.Enqueue(f);
                n++;
            }
            if (replace) _replace = true;
            if (worldTimeTicks > 0) _pendingWorldTimeTicks = worldTimeTicks;
            return n;
        }

        /// <summary>
        /// Atomically drains the queued import files, the pending replace flag and any requested
        /// world-time (simulation thread). Returns false when there is nothing to do.
        /// </summary>
        public bool TryTakeImportBatch(out List<CharacterFileDto> files, out bool replace, out long worldTimeTicks)
        {
            files = new List<CharacterFileDto>();
            while (_pending.TryDequeue(out var f)) files.Add(f);
            replace = _replace;
            if (replace) _replace = false;
            worldTimeTicks = _pendingWorldTimeTicks;
            _pendingWorldTimeTicks = 0;
            return files.Count > 0 || replace || worldTimeTicks > 0;
        }
    }
}
