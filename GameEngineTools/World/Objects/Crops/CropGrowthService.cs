// CropGrowthService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Objects.Crops
{
    using System;
    using System.Linq;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// World-level clock of the grain cycle: once per world hour it adds the day's heat to every sown field and, every few weeks, writes
    /// off the storage loss of dry-grain stocks. Driven by the simulation loop like <see cref="ObjectRespawnScheduler"/>.
    /// </summary>
    public sealed class CropGrowthService
    {
        private readonly IMutableWorldObjectProvider _provider;
        private readonly CropConfig _config;
        private WDateTime? _lastGrowth;
        private WDateTime? _lastStorageLoss;

        /// <summary>Creates the service over the mutable object provider.</summary>
        public CropGrowthService(IMutableWorldObjectProvider provider, IOptions<CropConfig> config)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(config);
            _provider = provider;
            _config = config.Value;
        }

        /// <summary>
        /// Advances every sown field by the heat of the time elapsed since the last call and, when due, applies storage loss.
        /// Does nothing until an hour of world time has passed since the previous work.
        /// </summary>
        /// <param name="now">Current simulation time.</param>
        /// <param name="temperatureC">The air temperature the fields feel now (the celestial context's ambient temperature).</param>
        public void Tick(WDateTime now, double temperatureC)
        {
            if (_lastGrowth is not { } last)
            {
                _lastGrowth = now;
                _lastStorageLoss = now;
                return;
            }

            var elapsed = WDateTime.Difference(now, last);
            if (elapsed.TotalHours < 1.0)
                return;

            var days = elapsed.TotalDays;
            var heat = CropMath.DegreeDays(temperatureC, days, _config);
            var sinceLoss = WDateTime.Difference(now, _lastStorageLoss!.Value).TotalDays;
            var lossDue = sinceLoss >= _config.StorageLossIntervalDays;
            var daysInYear = WWorld.Spec.HoursInYear(now.Year) / (double)WWorld.Spec.HoursPerDay;

            foreach (var locationId in _provider.GetKnownLocationIds().ToList())
            {
                foreach (var obj in _provider.GetAllObjectsAt(locationId).Where(o => o.HeldBy is null).ToList())
                {
                    if (obj.Field is { Stage: FieldStage.Sown } field)
                    {
                        var grown = CropMath.Grow(field, heat, now, _config);
                        if (grown != field)
                            Replace(obj with { Field = grown });
                    }
                    else if (lossDue && IsStore(obj))
                    {
                        var left = CropMath.AfterStorage(obj.Quantity, sinceLoss, daysInYear, _config);
                        if (left != obj.Quantity)
                            Replace(obj with { Quantity = Math.Max(1, left) });
                    }
                }
            }

            _lastGrowth = now;
            if (lossDue)
                _lastStorageLoss = now;
        }

        /// <summary>A stock of dry grain or flour kept in a settlement: not a production fixture, not shop stock, not held by anyone.</summary>
        public static bool IsStore(WorldObject o)
            => o.Category == WorldObjectCategory.Ambient
               && o.ItemKind is PickupItemKind.Grain or PickupItemKind.Flour
               && o.Price is null
               && !o.Affordances.Any(a => a.Type == AffordanceType.Production);

        private void Replace(WorldObject updated)
        {
            _provider.RemoveObject(updated.LocationId, updated.Id);
            _provider.AddObject(updated);
        }
    }
}
