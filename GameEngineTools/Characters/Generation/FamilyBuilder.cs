// FamilyBuilder.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Seeds pre-existing family bonds directly into the relationship graph of each character
    /// and registers the family topology into a <see cref="FamilyGraph"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is a <b>generation-time helper</b>, not a runtime engine. It is intended for
    /// world setup — either when loading a pre-authored family from data, or when handling
    /// a <c>ChildBorn</c> event that produces a newborn whose parent bonds must be established immediately.
    /// </para>
    /// <para>
    /// Each call is <b>idempotent</b>: calling <see cref="Wire"/> twice with the same characters
    /// overwrites the edges with identical values and adds no duplicate kin links to the graph.
    /// </para>
    /// <para>
    /// <b>Bond baselines (scientific rationale):</b>
    /// <list type="bullet">
    ///   <item>
    ///     <b>Partner bond</b> — Closeness 85, Trust 80, CommunalStrength 70, RomanticInterest 75.
    ///     Hazan &amp; Shaver 1987 pair-bond; Basson 2001 responsive desire baseline.
    ///   </item>
    ///   <item>
    ///     <b>Parent → child</b> — Trust 90, Comfort 90, Closeness 88, CommunalStrength 85.
    ///     Bowlby 1969 unconditional parental attachment.
    ///   </item>
    ///   <item>
    ///     <b>Child → parent</b> — Trust 82, Comfort 80, Closeness 78.
    ///     Asymmetric: attachment security lower than parental (Ainsworth 1978).
    ///   </item>
    ///   <item>
    ///     <b>Sibling bond</b> — Closeness 55, Trust 65, Familiarity 70.
    ///     Dunn 1983: siblings are simultaneously close and rivalrous.
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    public static class FamilyBuilder
    {
        #region Public API

        /// <summary>
        /// Wires partner, parent-child, and sibling bonds for a complete nuclear family
        /// and registers all members and links into the provided <see cref="FamilyGraph"/>.
        /// </summary>
        /// <param name="graph">Scene-level family registry to update.</param>
        /// <param name="partnerA">First parent / partner.</param>
        /// <param name="partnerB">Second parent / partner.</param>
        /// <param name="children">
        /// All children of this family unit. May be empty — wires only the partner bond in that case.
        /// </param>
        /// <param name="now">
        /// Current world time. Used as <c>LastContactTime</c> on all seeded edges so the
        /// Navarro gap rule starts from a fresh baseline.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="graph"/>, <paramref name="partnerA"/>,
        /// or <paramref name="partnerB"/> is <c>null</c>.
        /// </exception>
        public static void Wire(
            FamilyGraph graph,
            IHuman partnerA,
            IHuman partnerB,
            IReadOnlyList<IHuman> children,
            WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(partnerA);
            ArgumentNullException.ThrowIfNull(partnerB);
            ArgumentNullException.ThrowIfNull(children);

            // ── Register all members in the graph ─────────────────────────────
            graph.Register(partnerA);
            graph.Register(partnerB);

            foreach (var child in children)
            {
                if (child is not null)
                {
                    graph.Register(child);
                }
            }

            // ── Partner bond (bidirectional) ──────────────────────────────────
            SeedEdge(partnerA, partnerB, BuildPartnerEdge(partnerA.Id, partnerB.Id, partnerB.Biology, now), now);
            SeedEdge(partnerB, partnerA, BuildPartnerEdge(partnerB.Id, partnerA.Id, partnerA.Biology, now), now);
            graph.AddKinLink(partnerA.Id, partnerB.Id, KinRole.Partner);
            graph.AddKinLink(partnerB.Id, partnerA.Id, KinRole.Partner);

            // ── Parent ↔ child bonds ──────────────────────────────────────────
            foreach (var child in children)
            {
                if (child is null)
                {
                    continue;
                }

                SeedEdge(partnerA, child, BuildParentToChildEdge(partnerA.Id, child.Id, now), now);
                SeedEdge(partnerB, child, BuildParentToChildEdge(partnerB.Id, child.Id, now), now);
                SeedEdge(child, partnerA, BuildChildToParentEdge(child.Id, partnerA.Id, partnerA.Biology, now), now);
                SeedEdge(child, partnerB, BuildChildToParentEdge(child.Id, partnerB.Id, partnerB.Biology, now), now);

                graph.AddKinLink(partnerA.Id, child.Id, KinRole.Parent);
                graph.AddKinLink(partnerB.Id, child.Id, KinRole.Parent);
                graph.AddKinLink(child.Id, partnerA.Id, KinRole.Child);
                graph.AddKinLink(child.Id, partnerB.Id, KinRole.Child);
            }

            // ── Sibling bonds (all pairs) ─────────────────────────────────────
            for (var i = 0; i < children.Count; i++)
            {
                for (var j = i + 1; j < children.Count; j++)
                {
                    var sibA = children[i];
                    var sibB = children[j];

                    if (sibA is null || sibB is null)
                    {
                        continue;
                    }

                    SeedEdge(sibA, sibB, BuildSiblingEdge(sibA.Id, sibB.Id, now), now);
                    SeedEdge(sibB, sibA, BuildSiblingEdge(sibB.Id, sibA.Id, now), now);
                    graph.AddKinLink(sibA.Id, sibB.Id, KinRole.Sibling);
                    graph.AddKinLink(sibB.Id, sibA.Id, KinRole.Sibling);
                }
            }
        }

        /// <summary>
        /// Wires only the parent-child bond for a newborn and registers them in the graph.
        /// </summary>
        /// <remarks>
        /// Intended for use in the <c>ChildBorn</c> event handler inside <c>SimulationScene.OnTick</c>.
        /// Sibling bonds can be added later by calling <see cref="AddSiblingBond"/>.
        /// </remarks>
        /// <param name="graph">Scene-level family registry to update.</param>
        /// <param name="parentA">First parent.</param>
        /// <param name="parentB">Second parent.</param>
        /// <param name="newborn">The newly born child.</param>
        /// <param name="now">Current world time.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <c>null</c>.</exception>
        public static void WireNewborn(
            FamilyGraph graph,
            IHuman parentA,
            IHuman parentB,
            IHuman newborn,
            WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(parentA);
            ArgumentNullException.ThrowIfNull(parentB);
            ArgumentNullException.ThrowIfNull(newborn);

            graph.Register(newborn);

            SeedEdge(parentA, newborn, BuildParentToChildEdge(parentA.Id, newborn.Id, now), now);
            SeedEdge(parentB, newborn, BuildParentToChildEdge(parentB.Id, newborn.Id, now), now);
            SeedEdge(newborn, parentA, BuildChildToParentEdge(newborn.Id, parentA.Id, parentA.Biology, now), now);
            SeedEdge(newborn, parentB, BuildChildToParentEdge(newborn.Id, parentB.Id, parentB.Biology, now), now);

            graph.AddKinLink(parentA.Id, newborn.Id, KinRole.Parent);
            graph.AddKinLink(parentB.Id, newborn.Id, KinRole.Parent);
            graph.AddKinLink(newborn.Id, parentA.Id, KinRole.Child);
            graph.AddKinLink(newborn.Id, parentB.Id, KinRole.Child);
        }

        /// <summary>
        /// Wires a bidirectional sibling bond between two existing characters.
        /// </summary>
        /// <param name="graph">Scene-level family registry to update.</param>
        /// <param name="siblingA">First sibling.</param>
        /// <param name="siblingB">Second sibling.</param>
        /// <param name="now">Current world time.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <c>null</c>.</exception>
        public static void AddSiblingBond(
            FamilyGraph graph,
            IHuman siblingA,
            IHuman siblingB,
            WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(siblingA);
            ArgumentNullException.ThrowIfNull(siblingB);

            SeedEdge(siblingA, siblingB, BuildSiblingEdge(siblingA.Id, siblingB.Id, now), now);
            SeedEdge(siblingB, siblingA, BuildSiblingEdge(siblingB.Id, siblingA.Id, now), now);
            graph.AddKinLink(siblingA.Id, siblingB.Id, KinRole.Sibling);
            graph.AddKinLink(siblingB.Id, siblingA.Id, KinRole.Sibling);
        }

        /// <summary>
        /// Wires bidirectional grandparent bonds between a grandparent and a grandchild.
        /// </summary>
        /// <remarks>
        /// Call after <see cref="WireNewborn"/> once the grandparents are identified
        /// via <see cref="FamilyGraph.GetKin"/> with <see cref="KinRole.Parent"/>.
        /// </remarks>
        /// <param name="graph">Scene-level family registry to update.</param>
        /// <param name="grandparent">The grandparent character.</param>
        /// <param name="grandchild">The grandchild character.</param>
        /// <param name="now">Current world time.</param>
        /// <exception cref="ArgumentNullException">Thrown when any argument is <c>null</c>.</exception>
        public static void AddGrandparentBond(
            FamilyGraph graph,
            IHuman grandparent,
            IHuman grandchild,
            WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(graph);
            ArgumentNullException.ThrowIfNull(grandparent);
            ArgumentNullException.ThrowIfNull(grandchild);

            SeedEdge(grandparent, grandchild, BuildGrandparentEdge(grandparent.Id, grandchild.Id, now), now);
            SeedEdge(grandchild, grandparent, BuildGrandchildEdge(grandchild.Id, grandparent.Id, grandparent.Biology, now), now);
            graph.AddKinLink(grandparent.Id, grandchild.Id, KinRole.Grandparent);
            graph.AddKinLink(grandchild.Id, grandparent.Id, KinRole.Grandchild);
        }

        #endregion Public API

        #region Edge builders

        /// <summary>Builds the relationship edge that one partner holds toward the other.</summary>
        private static RelationshipEdge BuildPartnerEdge(
            HumanId self,
            HumanId other,
            SexBiology? otherBiology,
            WDateTime now)
            => new(
                A: self,
                B: other,
                Like: 82,
                Trust: 80,
                Familiarity: 85,
                AestheticAttraction: 60,
                PhysicalAttraction: 55,
                RomanticInterest: 75,
                SexualInterest: 55,
                Closeness: 85,
                Respect: 75,
                Comfort: 82,
                Breakdown: new DomainBreakdown(65, 65, 60, 72, 55),
                PositiveInteractionCount: 60,
                TargetBiology: otherBiology,
                CommunalStrength: 70,
                ExchangeStrength: 20,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 40,
                KinRole: KinRole.Partner);

        /// <summary>Builds the relationship edge that a parent holds toward their child.</summary>
        private static RelationshipEdge BuildParentToChildEdge(
            HumanId parent,
            HumanId child,
            WDateTime now)
            => new(
                A: parent,
                B: child,
                Like: 90,
                Trust: 90,
                Familiarity: 80,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 88,
                Respect: 70,
                Comfort: 90,
                Breakdown: new DomainBreakdown(50, 50, 0, 70, 0),
                PositiveInteractionCount: 40,
                TargetBiology: null,
                CommunalStrength: 85,
                ExchangeStrength: 0,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 0,
                KinRole: KinRole.Parent);

        /// <summary>Builds the relationship edge that a child holds toward a parent.</summary>
        private static RelationshipEdge BuildChildToParentEdge(
            HumanId child,
            HumanId parent,
            SexBiology? parentBiology,
            WDateTime now)
            => new(
                A: child,
                B: parent,
                Like: 82,
                Trust: 82,
                Familiarity: 80,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 78,
                Respect: 80,
                Comfort: 80,
                Breakdown: new DomainBreakdown(60, 55, 0, 75, 0),
                PositiveInteractionCount: 40,
                TargetBiology: parentBiology,
                CommunalStrength: 70,
                ExchangeStrength: 0,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 0,
                KinRole: KinRole.Child);

        /// <summary>Builds the relationship edge between two siblings.</summary>
        private static RelationshipEdge BuildSiblingEdge(
            HumanId self,
            HumanId sibling,
            WDateTime now)
            => new(
                A: self,
                B: sibling,
                Like: 65,
                Trust: 65,
                Familiarity: 70,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 55,
                Respect: 60,
                Comfort: 62,
                Breakdown: new DomainBreakdown(50, 55, 0, 60, 0),
                PositiveInteractionCount: 25,
                TargetBiology: null,
                CommunalStrength: 45,
                ExchangeStrength: 10,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 0,
                KinRole: KinRole.Sibling);

        /// <summary>Builds the relationship edge that a grandparent holds toward a grandchild.</summary>
        private static RelationshipEdge BuildGrandparentEdge(
            HumanId grandparent,
            HumanId grandchild,
            WDateTime now)
            => new(
                A: grandparent,
                B: grandchild,
                Like: 82,
                Trust: 78,
                Familiarity: 65,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 60,
                Respect: 65,
                Comfort: 75,
                Breakdown: new DomainBreakdown(45, 50, 0, 65, 0),
                PositiveInteractionCount: 20,
                TargetBiology: null,
                CommunalStrength: 60,
                ExchangeStrength: 0,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 0,
                KinRole: KinRole.Grandparent);

        /// <summary>Builds the relationship edge that a grandchild holds toward a grandparent.</summary>
        private static RelationshipEdge BuildGrandchildEdge(
            HumanId grandchild,
            HumanId grandparent,
            SexBiology? grandparentBiology,
            WDateTime now)
            => new(
                A: grandchild,
                B: grandparent,
                Like: 75,
                Trust: 72,
                Familiarity: 65,
                AestheticAttraction: 0,
                PhysicalAttraction: 0,
                RomanticInterest: 0,
                SexualInterest: 0,
                Closeness: 55,
                Respect: 75,
                Comfort: 70,
                Breakdown: new DomainBreakdown(50, 50, 0, 65, 0),
                PositiveInteractionCount: 20,
                TargetBiology: grandparentBiology,
                CommunalStrength: 50,
                ExchangeStrength: 0,
                TransgressionResidue: 0,
                LastContactTime: now,
                IsContemptuouslyDestroyed: false,
                ResponsiveDesireLevel: 0,
                KinRole: KinRole.Grandchild);

        #endregion Edge builders

        #region Private helpers

        /// <summary>
        /// Delivers a pre-built edge into the target character's relationship engine
        /// via <see cref="IHuman.ReceiveEvent"/> using a <see cref="FamilyBondSeeded"/> domain event.
        /// </summary>
        /// <remarks>
        /// Using <c>ReceiveEvent</c> keeps FamilyBuilder decoupled from engine internals —
        /// the engine processes <see cref="FamilyBondSeeded"/> in its <c>Handle</c> switch
        /// and writes the edge directly into its state dictionary.
        /// This adheres to the Dependency Inversion Principle: FamilyBuilder depends only
        /// on the <see cref="IHuman"/> interface, not on any concrete engine.
        /// </remarks>
        private static void SeedEdge(IHuman character, IHuman target, RelationshipEdge edge, WDateTime now)
        {
            character.ReceiveEvent(new FamilyBondSeeded(now, character.Id, target.Id, edge));
        }

        #endregion Private helpers
    }
}
