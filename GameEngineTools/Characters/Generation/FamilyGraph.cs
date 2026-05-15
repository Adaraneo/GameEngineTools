// FamilyGraph.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;

    // ── Supporting types ─────────────────────────────────────────────────────────

    /// <summary>
    /// A single directed kin link from one character to another.
    /// </summary>
    /// <param name="RelativeId">The character being pointed at.</param>
    /// <param name="Role">The role <em>this character</em> plays toward the relative.</param>
    public sealed record KinLink(HumanId RelativeId, KinRole Role);

    // ── FamilyGraph ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Central registry of family topology for the simulation scene.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="FamilyGraph"/> answers two classes of query efficiently:
    /// <list type="bullet">
    ///   <item>"Who are the members of the Ventifer family?" → <see cref="GetByName"/></item>
    ///   <item>"Who are the parents / children / siblings of character X?" → <see cref="GetKin"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// The graph is populated by <see cref="FamilyBuilder"/> at world-setup time and updated
    /// live during simulation via <see cref="Register"/> when new characters are born.
    /// It is held as a singleton on <c>SimulationScene</c>.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> not thread-safe. All mutations must occur on the simulation tick thread.
    /// </para>
    /// </remarks>
    public sealed class FamilyGraph
    {
        #region Private fields

        /// <summary>Surname (male form) → all living members of that family.</summary>
        private readonly Dictionary<string, List<HumanId>> _byName
            = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Character → directed kin links (parent, child, sibling, etc.).</summary>
        private readonly Dictionary<HumanId, List<KinLink>> _byCharacter = new();

        #endregion Private fields

        #region Public queries

        /// <summary>
        /// Returns all character IDs that share the given family name.
        /// </summary>
        /// <param name="familyName">
        /// The male form of the surname (e.g. <c>"Ventifer"</c>).
        /// Lookup is case-insensitive.
        /// </param>
        /// <returns>
        /// Read-only list of <see cref="HumanId"/> values, or an empty list when
        /// no characters with that name are registered.
        /// </returns>
        public IReadOnlyList<HumanId> GetByName(string familyName)
        {
            return _byName.TryGetValue(familyName, out var list)
                ? list.AsReadOnly()
                : Array.Empty<HumanId>();
        }

        /// <summary>
        /// Returns all directed kin links for the given character.
        /// </summary>
        /// <param name="characterId">The character whose kin is requested.</param>
        /// <returns>
        /// Read-only list of <see cref="KinLink"/> records, or an empty list when
        /// the character has no registered family.
        /// </returns>
        public IReadOnlyList<KinLink> GetKin(HumanId characterId)
        {
            return _byCharacter.TryGetValue(characterId, out var list)
                ? list.AsReadOnly()
                : Array.Empty<KinLink>();
        }

        /// <summary>
        /// Returns all kin of the given character that match the specified role.
        /// </summary>
        /// <param name="characterId">The character whose kin is requested.</param>
        /// <param name="role">The kin role to filter by (e.g. <see cref="KinRole.Parent"/>).</param>
        /// <returns>
        /// Filtered read-only sequence of <see cref="KinLink"/> records.
        /// </returns>
        public IEnumerable<KinLink> GetKin(HumanId characterId, KinRole role)
            => GetKin(characterId).Where(k => k.Role == role);

        /// <summary>
        /// Returns <c>true</c> when the two characters share any registered kin bond.
        /// </summary>
        /// <param name="a">First character.</param>
        /// <param name="b">Second character.</param>
        public bool AreRelated(HumanId a, HumanId b)
            => GetKin(a).Any(k => k.RelativeId == b);

        /// <summary>
        /// Returns <c>true</c> when the two characters share the specified kin bond
        /// in the direction A → B.
        /// </summary>
        /// <param name="a">The observer character.</param>
        /// <param name="b">The target character.</param>
        /// <param name="role">The expected role of A toward B.</param>
        public bool AreRelated(HumanId a, HumanId b, KinRole role)
            => GetKin(a).Any(k => k.RelativeId == b && k.Role == role);


        /// <summary>
        /// Returns all character IDs that share the given family name
        /// AND have at least one registered kin bond — i.e. are actual clan members.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is the preferred method for querying clan membership.
        /// Unlike <see cref="GetByName"/>, it excludes characters who merely share
        /// the surname by coincidence (e.g. a randomly generated stranger named "Jan Ventifer"
        /// who has no family connections within the scene).
        /// </para>
        /// <para>
        /// A character is considered a clan member when they are registered under
        /// <paramref name="familyName"/> AND their kin link list is non-empty.
        /// The kin links are populated exclusively by <see cref="FamilyBuilder"/> —
        /// so only characters who were explicitly wired into a family structure qualify.
        /// </para>
        /// </remarks>
        /// <param name="familyName">
        /// The male form of the surname (e.g. <c>"Ventifer"</c>).
        /// Lookup is case-insensitive.
        /// </param>
        /// <returns>
        /// Read-only list of <see cref="HumanId"/> values for confirmed clan members,
        /// or an empty list when no such members exist.
        /// </returns>
        public IReadOnlyList<HumanId> GetClanMembers(string familyName)
        {
            if (!_byName.TryGetValue(familyName, out var members))
            {
                return Array.Empty<HumanId>();
            }

            // Filter to only those who have at least one KinLink registered —
            // meaning FamilyBuilder explicitly wired them into a family.
            return members
                .Where(id => _byCharacter.TryGetValue(id, out var links) && links.Count > 0)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// Returns <c>true</c> when the character belongs to the named clan —
        /// i.e. shares the surname AND has at least one registered kin bond.
        /// </summary>
        /// <param name="characterId">The character to check.</param>
        /// <param name="familyName">
        /// The male form of the surname (e.g. <c>"Ventifer"</c>).
        /// Lookup is case-insensitive.
        /// </param>
        /// <returns>
        /// <c>true</c> if the character is a confirmed clan member; <c>false</c> otherwise.
        /// </returns>
        public bool IsClanMember(HumanId characterId, string familyName)
        {
            if (!_byName.TryGetValue(familyName, out var members))
            {
                return false;
            }

            // Must be in the surname bucket AND have at least one kin link.
            return members.Contains(characterId)
                && _byCharacter.TryGetValue(characterId, out var links)
                && links.Count > 0;
        }

        #endregion Public queries

        #region Mutations

        /// <summary>
        /// Registers a character in the family graph under their surname,
        /// without adding any kin links.
        /// </summary>
        /// <remarks>
        /// Call this when a character is added to the scene and has no family yet
        /// (e.g. a randomly generated stranger). <see cref="FamilyBuilder"/> calls
        /// <see cref="AddKinLink"/> separately for the actual bonds.
        /// </remarks>
        /// <param name="character">The character to register.</param>
        public void Register(IHuman character)
        {
            ArgumentNullException.ThrowIfNull(character);

            var name = character.Identity.LastName.Male;

            if (!_byName.TryGetValue(name, out var members))
            {
                members = new List<HumanId>();
                _byName[name] = members;
            }

            if (!members.Contains(character.Id))
            {
                members.Add(character.Id);
            }

            // Ensure a kin entry exists even if no links are added yet.
            if (!_byCharacter.ContainsKey(character.Id))
            {
                _byCharacter[character.Id] = new List<KinLink>();
            }
        }

        /// <summary>
        /// Adds a directed kin link from <paramref name="owner"/> to <paramref name="relative"/>.
        /// </summary>
        /// <remarks>
        /// Called by <see cref="FamilyBuilder"/> after seeding edges.
        /// Duplicate links (same relative and same role) are silently ignored.
        /// </remarks>
        /// <param name="owner">The character who holds the kin link.</param>
        /// <param name="relative">The character being pointed at.</param>
        /// <param name="role">The role <paramref name="owner"/> plays toward <paramref name="relative"/>.</param>
        public void AddKinLink(HumanId owner, HumanId relative, KinRole role)
        {
            if (!_byCharacter.TryGetValue(owner, out var links))
            {
                links = new List<KinLink>();
                _byCharacter[owner] = links;
            }

            // Guard against duplicate links — FamilyBuilder may be called multiple times
            // (e.g. for successive children) and must remain idempotent.
            if (!links.Any(k => k.RelativeId == relative && k.Role == role))
            {
                links.Add(new KinLink(relative, role));
            }
        }

        /// <summary>
        /// Removes a character from all family records.
        /// </summary>
        /// <remarks>
        /// Call when a character dies or is permanently removed from the scene.
        /// Removes the character from their surname bucket and from all kin lists
        /// that reference them.
        /// </remarks>
        /// <param name="characterId">The character to deregister.</param>
        public void Deregister(HumanId characterId)
        {
            // Remove from surname bucket.
            foreach (var members in _byName.Values)
            {
                members.Remove(characterId);
            }

            // Remove the character's own kin list.
            _byCharacter.Remove(characterId);

            // Remove all incoming links that pointed at this character.
            foreach (var links in _byCharacter.Values)
            {
                links.RemoveAll(k => k.RelativeId == characterId);
            }
        }

        #endregion Mutations
    }
}
