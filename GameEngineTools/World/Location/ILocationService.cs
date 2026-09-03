// ILocationService.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Location
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Interactions;
    using GameEngineTools.World.Utils.Time;

    #region Location data types

    /// <summary>
    /// Broad category of a location — used by <see cref="ILocationService.GetLocationsByType"/>
    /// and by <see cref="GameEngineTools.Characters.Engines.Behavior.DefaultBehaviorEngine"/> when emitting <c>MoveTo:*</c> actions.
    /// </summary>
    public enum LocationType
    {
        /// <summary>Open social spaces — tavern, square, market.</summary>
        Social,

        /// <summary>Quiet spaces for focus or intimacy — library, private room.</summary>
        Private,

        /// <summary>Spaces tied to productive activity — workshop, forge, study.</summary>
        Work,

        /// <summary>Spaces for recovery — inn, home.</summary>
        Rest,

        /// <summary>Large public spaces with no dominant character — roads, fields.</summary>
        Public
    }

    /// <summary>
    /// Static descriptor of a named location in the world.
    /// Defines the baseline acoustic and social character of a place
    /// before any characters arrive.
    /// </summary>
    /// <param name="Id">Unique location identifier (e.g. "tavern", "castle_hall").</param>
    /// <param name="DisplayName">Human-readable name used in narrative output.</param>
    /// <param name="BaseNoise">
    /// Ambient noise level before any characters are present. Range [0, 1].
    /// A library might be 0.05; a smithy 0.7.
    /// </param>
    /// <param name="NoisePerPerson">
    /// How much each additional character raises noise. Range [0, 1].
    /// A large hall has small per-person contribution; a small room has large.
    /// </param>
    /// <param name="Capacity">
    /// "Comfortable" capacity — how many characters fit before crowding becomes notable.
    /// Crowding = characterCount / Capacity, clamped to [0, 1].
    /// </param>
    /// <param name="AllowsPrivacy">
    /// Whether this location can ever be considered private.
    /// A public square never allows privacy regardless of character count.
    /// </param>
    /// <param name="X">
    /// Position in a local 2D Cartesian world-space, in meters. <c>0.0</c> (the default) means
    /// unpositioned. Roughly consistent with connected locations' <c>DistanceMeters</c>.
    /// </param>
    /// <param name="Y">See <paramref name="X"/>.</param>
    /// <param name="AltitudeMeters">
    /// Elevation above sea level, in meters. <c>0.0</c> (the default) means sea level / unset.
    /// Authored by the terrain heightmap tool (see <c>TerrainHeightmap</c>), sampled at
    /// (<paramref name="X"/>, <paramref name="Y"/>). Mirrors
    /// <see cref="GameEngineTools.Characters.Core.EnginesSnapshot.AltitudeMeters"/>, which the
    /// simulation layer is expected to populate from this field per character's current
    /// location (not yet wired — see project plan).
    /// </param>
    /// <param name="TemperatureCelsius">
    /// Ambient temperature at this location's position, derived from latitude and altitude by
    /// WorldGen's climate model (see <c>WorldGen.Generation.ClimateModel</c>). <c>0.0</c> (the
    /// default) means unset — locations authored by hand (indoor rooms, houses) never populate
    /// this. Not yet consumed by any engine (see project plan) — stored for future use, e.g. by
    /// Physiology's cold/heat exposure or clothing choice.
    /// </param>
    /// <param name="Humidity">
    /// Ambient relative wetness [0, 1] at this location's position, from an independent noise
    /// field (see <c>WorldGen.Generation.ClimateModel</c>). <c>0.0</c> (the default) means unset.
    /// Drives Desert/Savanna/Jungle biome classification alongside <paramref name="TemperatureCelsius"/>;
    /// not yet consumed by any engine beyond that.
    /// </param>
    public sealed record LocationDescriptor(
        string Id,
        string DisplayName,
        double BaseNoise,
        double NoisePerPerson,
        int Capacity,
        bool AllowsPrivacy,
        LocationType Type,
        TerrainType Terrain = TerrainType.Indoor,
        double DangerLevel = 0.0,
        bool AllowsPickup = true,
        /// <summary>
        /// Optional social norm context id referencing <c>SocialNorms.Id</c> in the world database.
        /// <c>null</c> means no active norm constraint at this location.
        /// </summary>
        string? NormId = null,
        double X = 0.0,
        double Y = 0.0,
        double AltitudeMeters = 0.0,
        double TemperatureCelsius = 0.0,
        double Humidity = 0.0);

    #endregion Location data types

    #region ILocationService

    /// <summary>
    /// Tracks which characters are in which location and computes
    /// the resulting <see cref="InteractionSurface"/> (noise, crowding, privacy).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Separation of concerns:</b><br/>
    /// The engine layer (<see cref="IInteractionEngine"/>) only consumes
    /// <see cref="ContextChanged"/> events — it has no knowledge of locations.
    /// This service is the bridge between the world layer and the engine layer.
    /// </para>
    /// <para>
    /// <b>Who calls this?</b><br/>
    /// <see cref="GameEngineTools.World.Simulation.SimulationScene"/> calls <see cref="DispatchContextEvents"/>
    /// once per tick, before characters tick. This ensures every character
    /// has an up-to-date <see cref="InteractionSurface"/> before any
    /// <see cref="InteractionProposed"/> is evaluated.
    /// </para>
    /// </remarks>
    public interface ILocationService
    {
        /// <summary>
        /// Registers a location descriptor so the service knows its static properties.
        /// Must be called before any character is moved to this location.
        /// </summary>
        /// <param name="descriptor">Static description of the location.</param>
        void RegisterLocation(LocationDescriptor descriptor);

        /// <summary>
        /// Moves a character to the specified location.
        /// Replaces any previous location assignment for this character.
        /// </summary>
        /// <param name="characterId">The character being moved.</param>
        /// <param name="locationId">Target location identifier.</param>
        void MoveCharacter(HumanId characterId, string locationId);

        /// <summary>
        /// Removes a character's location assignment, leaving it unplaced (e.g. while travelling
        /// between locations). <see cref="GetLocation"/> returns <c>null</c> afterwards and the
        /// character is excluded from <see cref="GetCharactersAt"/> and crowding counts.
        /// The next <see cref="DispatchContextEvents"/> emits a one-shot "left location"
        /// <see cref="ContextChanged"/> (empty <c>Location</c>) so engines stop reading the old surface.
        /// No-op if the character is not currently placed.
        /// </summary>
        /// <param name="characterId">The character to unplace.</param>
        void RemoveCharacter(HumanId characterId);

        /// <summary>
        /// Emits a <see cref="ContextChanged"/> event to every character
        /// whose current location has changed since the last dispatch,
        /// or to all characters if <paramref name="forceAll"/> is true.
        /// </summary>
        /// <param name="now">Current simulation time.</param>
        /// <param name="characters">All characters in the scene.</param>
        /// <param name="forceAll">
        /// When true, dispatches to all characters regardless of whether
        /// their location changed. Useful on first tick.
        /// </param>
        void DispatchContextEvents(
            WDateTime now,
            IReadOnlyList<IHuman> characters,
            bool forceAll = false);

        /// <summary>
        /// Returns the current location id for the given character,
        /// or null if the character has not been placed yet.
        /// </summary>
        string? GetLocation(HumanId characterId);

        /// <summary>
        /// Returns the ids of all characters currently assigned to the given location.
        /// Returns an empty collection if the location is unknown or has no characters.
        /// </summary>
        /// <param name="locationId">The location to query.</param>
        IReadOnlyList<HumanId> GetCharactersAt(string locationId);

        /// <summary>
        /// Returns the ids of all registered locations with the given type.
        /// Returns an empty list if no locations of that type have been registered.
        /// </summary>
        /// <param name="type">The location type to query.</param>
        IReadOnlyList<string> GetLocationsByType(LocationType type);

        /// <summary>Returns the descriptor for a location id, or <c>null</c> if unknown.</summary>
        /// <param name="locationId">The location id to resolve.</param>
        LocationDescriptor? GetDescriptor(string locationId);
    }

    #endregion ILocationService
}
