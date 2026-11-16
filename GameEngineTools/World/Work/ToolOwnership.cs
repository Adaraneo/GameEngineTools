// ToolOwnership.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;

    /// <summary>Gives a share of the tools of a new world to characters; the rest stay common property (owned by nobody).</summary>
    public static class ToolOwnership
    {
        /// <summary>Default share of the tools that become personal (a design pick); the rest stay common.</summary>
        public const double DefaultPersonalShare = 1.0 / 3.0;

        /// <summary>Makes every n-th unowned, unheld tool the property of one of the owners (round-robin), so about <paramref name="share"/> of them are personal; returns how many were assigned.</summary>
        public static int AssignPersonalTools(IMutableWorldObjectProvider provider, IReadOnlyList<HumanId> owners, double share = DefaultPersonalShare)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (owners.Count == 0 || share <= 0.0)
            {
                return 0;
            }

            var tools = provider.GetAllObjects()
                .Where(o => o.Category == WorldObjectCategory.Tool && o.OwnerId is null && o.HeldBy is null)
                .OrderBy(o => o.Id, StringComparer.Ordinal)
                .ToList();
            var personal = (int)Math.Floor(tools.Count * Math.Min(share, 1.0));
            if (personal == 0)
            {
                return 0;
            }

            var step = (double)tools.Count / personal;
            for (var i = 0; i < personal; i++)
            {
                var tool = tools[(int)(i * step)];
                provider.RemoveObject(tool.LocationId, tool.Id);
                provider.AddObject(tool with { OwnerId = owners[i % owners.Count] });
            }

            return personal;
        }
    }
}
