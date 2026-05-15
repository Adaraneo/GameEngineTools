// NuclearFamily.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// A fully generated and relationship-wired nuclear family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All characters in this record are ready to be added to a <c>SimulationScene</c>.
    /// Their relationship edges have already been seeded by <see cref="FamilyBuilder"/>
    /// and all members are registered in the provided <see cref="FamilyGraph"/>.
    /// </para>
    /// <para>
    /// The typical usage pattern is:
    /// <code>
    /// var family = generator.Generate(spec, graph, now);
    /// scene.AddCharacters(family.AllMembers);
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="PartnerA">First parent / partner.</param>
    /// <param name="PartnerB">Second parent / partner.</param>
    /// <param name="Children">
    /// All children in birth order (oldest first, matching the order in <see cref="NuclearFamilySpec.Children"/>).
    /// </param>
    public sealed record NuclearFamily(
        IHuman PartnerA,
        IHuman PartnerB,
        IReadOnlyList<IHuman> Children)
    {
        /// <summary>
        /// Returns all family members as a flat list: PartnerA, PartnerB, then Children in order.
        /// </summary>
        /// <remarks>
        /// Use this to add the entire family to a scene in a single call.
        /// </remarks>
        public IReadOnlyList<IHuman> AllMembers
        {
            get
            {
                var all = new List<IHuman>(2 + Children.Count) { PartnerA, PartnerB };
                all.AddRange(Children);
                return all.AsReadOnly();
            }
        }
    }
}
