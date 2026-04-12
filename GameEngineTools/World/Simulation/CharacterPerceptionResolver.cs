// CharacterPerceptionResolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Resolves which characters are perceptually available to an observer under the current
    /// runtime perception fidelity and local interaction conditions.
    /// </summary>
    public static class CharacterPerceptionResolver
    {
        /// <summary>
        /// Gets a salience score for a target from the observer's current relational state.
        /// </summary>
        /// <param name="observer">Observer whose subjective salience is evaluated.</param>
        /// <param name="target">Candidate target.</param>
        /// <returns>Relative salience score. Higher means more likely to be noticed.</returns>
        public static double GetPerceptionSalience(IHuman observer, IHuman target)
        {
            var edge = observer.Snapshot.Relationships.Edges.GetValueOrDefault(target.Id);

            if (edge is null)
            {
                return 5.0;
            }

            return
                (edge.Closeness * 0.35) +
                (edge.Trust * 0.20) +
                (edge.Familiarity * 0.15) +
                (edge.RomanticInterest * 0.20) +
                (edge.SexualInterest * 0.10);
        }

        /// <summary>
        /// Returns the subset of characters in the observer's location that are perceptually
        /// available under the current runtime fidelity and interaction surface conditions.
        /// </summary>
        /// <param name="observer">The observing character.</param>
        /// <param name="characters">All candidate characters in the current scene.</param>
        /// <param name="locationService">Location service used to resolve co-location.</param>
        /// <param name="perceptionPolicy">Runtime perception fidelity policy.</param>
        /// <returns>Perceived characters ordered by descending salience.</returns>
        public static IReadOnlyList<IHuman> GetPerceivedCharacters(
            IHuman observer,
            IReadOnlyList<IHuman> characters,
            ILocationService locationService,
            IPerceptionFidelityPolicy perceptionPolicy,
            CharacterPerceptionOptions options)
        {
            ArgumentNullException.ThrowIfNull(observer);
            ArgumentNullException.ThrowIfNull(characters);
            ArgumentNullException.ThrowIfNull(locationService);
            ArgumentNullException.ThrowIfNull(perceptionPolicy);
            ArgumentNullException.ThrowIfNull(options);

            var observerLocation = locationService.GetLocation(observer.Id);
            if (observerLocation is null)
            {
                return Array.Empty<IHuman>();
            }

            var sameLocation = characters
                .Where(c => c.Id != observer.Id)
                .Where(c => string.Equals(
                    locationService.GetLocation(c.Id),
                    observerLocation,
                    StringComparison.Ordinal))
                .OrderByDescending(c => GetPerceptionSalience(observer, c))
                .ToList();

            if (sameLocation.Count == 0)
            {
                return sameLocation;
            }

            var fidelity = perceptionPolicy.GetLevel(observer.Id);
            var interactionSurface = observer.Snapshot.InteractionSurface;

            return fidelity switch
            {
                PerceptionFidelityLevel.Full => sameLocation,

                PerceptionFidelityLevel.LocalOnly => ResolveLocalOnly(
                    sameLocation,
                    interactionSurface.Noise,
                    interactionSurface.Crowding,
                    options),

                PerceptionFidelityLevel.Coarse => ResolveCoarse(
                    sameLocation,
                    interactionSurface.Noise,
                    interactionSurface.Crowding,
                    options),

                _ => sameLocation
            };
        }

        private static IReadOnlyList<IHuman> ResolveLocalOnly(
            IReadOnlyList<IHuman> sameLocation,
            double noise,
            double crowding,
            CharacterPerceptionOptions options)
        {
            if (noise > options.LocalOnlyNoiseThreshold
                || crowding > options.LocalOnlyCrowdingThreshold)
            {
                return Array.Empty<IHuman>();
            }

            return sameLocation
                .Take(Math.Max(0, options.MaxLocalOnlyTargets))
                .ToList();
        }

        private static IReadOnlyList<IHuman> ResolveCoarse(
            IReadOnlyList<IHuman> sameLocation,
            double noise,
            double crowding,
            CharacterPerceptionOptions options)
        {
            if (noise > options.CoarseNoiseThreshold
                || crowding > options.CoarseCrowdingThreshold)
            {
                return Array.Empty<IHuman>();
            }

            return sameLocation
                .Take(Math.Max(0, options.MaxCoarseTargets))
                .ToList();
        }
    }
}
