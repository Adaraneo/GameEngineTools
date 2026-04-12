// SceneCharacterLodResolver.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Location;

    /// <summary>
    /// Resolves scene-level runtime LOD for characters relative to a current focus character.
    /// </summary>
    public static class SceneCharacterLodResolver
    {
        /// <summary>
        /// Resolves the runtime cognitive resolution level for a scene participant.
        /// </summary>
        /// <param name="character">Candidate character whose runtime LOD is being resolved.</param>
        /// <param name="focusCharacterId">Current focus character id.</param>
        /// <param name="locationService">Location service used to compare co-location.</param>
        /// <param name="alwaysPlayer">Optional ids that should always run at player fidelity.</param>
        /// <returns>The resolved cognitive resolution level.</returns>
        public static CognitiveResolutionLevel Resolve(
            IHuman character,
            HumanId focusCharacterId,
            ILocationService locationService,
            IReadOnlySet<HumanId>? alwaysPlayer = null)
        {
            ArgumentNullException.ThrowIfNull(character);
            ArgumentNullException.ThrowIfNull(locationService);

            if (alwaysPlayer is not null && alwaysPlayer.Contains(character.Id))
            {
                return CognitiveResolutionLevel.Player;
            }

            if (character.Id == focusCharacterId)
            {
                return CognitiveResolutionLevel.Player;
            }

            var focusLocation = locationService.GetLocation(focusCharacterId);
            var characterLocation = locationService.GetLocation(character.Id);

            if (focusLocation is not null
                && characterLocation is not null
                && string.Equals(focusLocation, characterLocation, StringComparison.Ordinal))
            {
                return CognitiveResolutionLevel.Nearby;
            }

            return CognitiveResolutionLevel.Background;
        }
    }
}
