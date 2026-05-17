// ObjectRespawnScheduler.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects
{
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Checks all consumed world objects each simulation tick and restores those whose
    /// respawn timer has elapsed. Driven by the simulation loop (e.g. <c>SimulationScene</c>).
    /// </summary>
    public sealed class ObjectRespawnScheduler
    {
        private readonly CsvWorldObjectProvider _provider;

        public ObjectRespawnScheduler(CsvWorldObjectProvider provider)
        {
            _provider = provider;
        }

        /// <summary>
        /// Inspect all consumed objects and restore any that have exceeded their
        /// <see cref="WorldObject.RespawnMinutes"/> timer.
        /// </summary>
        public void Tick(WDateTime now)
        {
            foreach (var locationId in _provider.GetKnownLocationIds())
            {
                // GetAllObjectsAt includes consumed objects
                var all = _provider.GetAllObjectsAt(locationId).ToList();
                foreach (var obj in all)
                {
                    if (obj.ConsumedAt is not null && obj.Respawns)
                    {
                        var elapsed = WDateTime.Difference(now, obj.ConsumedAt.Value).TotalMinutes;
                        if (elapsed >= obj.RespawnMinutes)
                            _provider.RestoreObject(locationId, obj.Id);
                    }
                }
            }
        }
    }
}
