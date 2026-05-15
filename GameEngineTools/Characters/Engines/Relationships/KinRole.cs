// KinRole.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Relationships
{
    /// <summary>
    /// Identifies the family bond type carried on a <see cref="RelationshipEdge"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="KinRole"/> is a structural label — it does not replace the numeric dimensions
    /// on <see cref="RelationshipEdge"/> but makes family topology queryable without inspecting
    /// raw edge values (which are ambiguous for cross-orientation or estranged families).
    /// </para>
    /// <para>
    /// The role is set by <see cref="GameEngineTools.Characters.Generation.FamilyBuilder"/>
    /// at world-setup time and is never updated by the engine at runtime.
    /// It is therefore safe to use as a stable graph key.
    /// </para>
    /// </remarks>
    public enum KinRole
    {
        /// <summary>No family relationship — ordinary social edge.</summary>
        None,

        /// <summary>
        /// Romantic or spousal partner.
        /// Bidirectional: both A→B and B→A carry <see cref="Partner"/>.
        /// </summary>
        Partner,

        /// <summary>
        /// A is the biological or adoptive parent of B.
        /// Directed: only the parent→child edge carries this role.
        /// </summary>
        Parent,

        /// <summary>
        /// A is the biological or adoptive child of B.
        /// Directed: only the child→parent edge carries this role.
        /// </summary>
        Child,

        /// <summary>
        /// A and B share at least one common parent.
        /// Bidirectional: both sibling edges carry this role.
        /// </summary>
        Sibling,

        /// <summary>
        /// A is the grandparent of B.
        /// Directed: only the grandparent→grandchild edge carries this role.
        /// </summary>
        Grandparent,

        /// <summary>
        /// A is the grandchild of B.
        /// Directed: only the grandchild→grandparent edge carries this role.
        /// </summary>
        Grandchild,
    }
}
