// ToolOwnership.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;

    /// <summary>Gives a share of the tools of a new world to characters; the rest stay common property (owned by nobody). Who gets a tool depends on where it lies, never on anyone's occupation.</summary>
    public static class ToolOwnership
    {
        /// <summary>Default share of the tools that become personal (a design pick); the rest stay common.</summary>
        public const double DefaultPersonalShare = 1.0 / 3.0;

        /// <summary>
        /// Makes every n-th unowned, unheld tool the property of the person who lives nearest to it along the roads, so about <paramref name="share"/> of them are personal; returns how many were assigned.
        /// Among people sharing the nearest home the one with the fewest tools so far takes it; a tool no home can reach goes to the person with the fewest overall.
        /// </summary>
        /// <param name="provider">The world objects.</param>
        /// <param name="map">The road graph the distance is measured on.</param>
        /// <param name="residents">The candidate owners with their home locations.</param>
        /// <param name="share">Share of the tools that become personal, 0 to 1.</param>
        public static int AssignPersonalTools(IMutableWorldObjectProvider provider, WorldMap map, IReadOnlyList<(HumanId Id, string HomeLocationId)> residents, double share = DefaultPersonalShare)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(map);
            if (residents.Count == 0 || share <= 0.0)
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

            var nearestHome = NearestHomes(map, residents.Select(r => r.HomeLocationId).Distinct(StringComparer.Ordinal));
            var load = residents.ToDictionary(r => r.Id, _ => 0);
            var step = (double)tools.Count / personal;
            for (var i = 0; i < personal; i++)
            {
                var tool = tools[(int)(i * step)];
                var candidates = nearestHome.TryGetValue(tool.LocationId, out var home)
                    ? residents.Where(r => r.HomeLocationId == home).ToList()
                    : residents.ToList();
                var owner = candidates.OrderBy(r => load[r.Id]).First().Id;
                load[owner]++;
                provider.RemoveObject(tool.LocationId, tool.Id);
                provider.AddObject(tool with { OwnerId = owner });
            }

            return personal;
        }

        /// <summary>Multi-source Dijkstra: for every location reachable from a home, the home that is nearest to it.</summary>
        private static Dictionary<string, string> NearestHomes(WorldMap map, IEnumerable<string> homes)
        {
            var best = new Dictionary<string, double>(StringComparer.Ordinal);
            var label = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new PriorityQueue<string, double>();
            foreach (var home in homes)
            {
                best[home] = 0.0;
                label[home] = home;
                queue.Enqueue(home, 0.0);
            }

            while (queue.TryDequeue(out var node, out var distance))
            {
                if (distance > best[node])
                {
                    continue;
                }

                foreach (var connection in map.GetConnections(node))
                {
                    var next = distance + connection.DistanceMeters;
                    if (best.TryGetValue(connection.TargetLocationId, out var known) && known <= next)
                    {
                        continue;
                    }

                    best[connection.TargetLocationId] = next;
                    label[connection.TargetLocationId] = label[node];
                    queue.Enqueue(connection.TargetLocationId, next);
                }
            }

            return label;
        }
    }
}
