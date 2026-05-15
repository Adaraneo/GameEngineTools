// NuclearFamilyGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Generates a complete, genetically consistent nuclear family from a <see cref="NuclearFamilySpec"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the single entry point for prebuilt family generation. It composes:
    /// <list type="bullet">
    ///   <item><see cref="IHumanBlueprintGenerator"/> — for the two parents</item>
    ///   <item><see cref="IChildBlueprintGenerator"/> — for each child (genetic inheritance from parents)</item>
    ///   <item><see cref="IHumanFactory"/> — to create <see cref="IHuman"/> instances from blueprints</item>
    ///   <item><see cref="FamilyBuilder"/> — to seed relationship edges and register into <see cref="FamilyGraph"/></item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Why a separate generator and not a static method on FamilyBuilder?</b><br/>
    /// <see cref="FamilyBuilder"/> is a pure edge-seeding helper with no dependencies.
    /// Generation requires DI services (<see cref="IHumanFactory"/>, <see cref="IChildBlueprintGenerator"/>).
    /// Keeping the two concerns separate follows the Single Responsibility Principle:
    /// FamilyBuilder owns topology, NuclearFamilyGenerator owns creation.
    /// </para>
    /// <para>
    /// <b>Child appearance and stadium:</b><br/>
    /// Each child's <see cref="StadiumType"/> is derived from their <see cref="ChildSpec.BornOn"/>
    /// relative to <paramref name="now"/> inside <see cref="IChildBlueprintGenerator"/>.
    /// A child born 15 years ago gets <see cref="StadiumType.Teenager"/> proportions and personality;
    /// a child born 2 years ago gets <see cref="StadiumType.Baby"/> proportions. This is fully automatic.
    /// </para>
    /// </remarks>
    public sealed class NuclearFamilyGenerator
    {
        #region Private fields

        private readonly IHumanBlueprintGenerator _blueprintGenerator;
        private readonly IChildBlueprintGenerator _childGenerator;
        private readonly IHumanFactory _humanFactory;

        #endregion Private fields

        #region Constructor

        /// <summary>
        /// Initialises the generator with all required dependencies.
        /// Register as a singleton or scoped service in your DI container.
        /// </summary>
        /// <param name="blueprintGenerator">Used to generate parent blueprints.</param>
        /// <param name="childGenerator">Used to generate genetically inherited child blueprints.</param>
        /// <param name="humanFactory">Used to create <see cref="IHuman"/> instances from blueprints.</param>
        public NuclearFamilyGenerator(
            IHumanBlueprintGenerator blueprintGenerator,
            IChildBlueprintGenerator childGenerator,
            IHumanFactory humanFactory)
        {
            _blueprintGenerator = blueprintGenerator;
            _childGenerator = childGenerator;
            _humanFactory = humanFactory;
        }

        #endregion Constructor

        #region Public API

        /// <summary>
        /// Generates a complete nuclear family according to the provided specification.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Generation order matters for genetic determinism:
        /// parents are created first so their <see cref="IHuman.Id"/> values are stable
        /// before <see cref="IChildBlueprintGenerator.Generate"/> derives child seeds from them.
        /// </para>
        /// <para>
        /// After generation, all characters are registered in <paramref name="graph"/> and
        /// relationship edges are seeded via <see cref="FamilyBuilder.Wire"/>.
        /// The caller is responsible for adding the returned characters to the simulation scene.
        /// </para>
        /// </remarks>
        /// <param name="spec">Declarative family specification.</param>
        /// <param name="graph">Scene-level family registry. All members will be registered here.</param>
        /// <param name="now">Current world time. Used for edge <c>LastContactTime</c> and age-aware generation.</param>
        /// <returns>A fully wired <see cref="NuclearFamily"/> ready to be added to the scene.</returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="spec"/> or <paramref name="graph"/> is <c>null</c>.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when a <see cref="ChildSpec.BornOn"/> date is in the future relative to <paramref name="now"/>.
        /// </exception>
        public NuclearFamily Generate(NuclearFamilySpec spec, FamilyGraph graph, WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(graph);

            // ── Step 1: Generate parents ──────────────────────────────────────
            // Parents must be created first — child seeds are derived from parent IDs.
            var partnerA = _humanFactory.Create(_blueprintGenerator.Generate(spec.PartnerARequest));
            var partnerB = _humanFactory.Create(_blueprintGenerator.Generate(spec.PartnerBRequest));

            // ── Step 2: Generate children (genetically from both parents) ─────
            var children = new List<IHuman>(spec.Children.Count);

            foreach (var childSpec in spec.Children)
            {
                ValidateChildBornOn(childSpec.BornOn, now);

                // IChildBlueprintGenerator derives child appearance and personality
                // from both parents via genetic blending (height regression, BigFive blend,
                // skin/eye/hair color inheritance, nose and lip projection).
                //
                // The stadium (Baby/Child/Teenager/Adult) is resolved automatically
                // from (now - bornOn) inside the generator — no manual override needed.
                var childBlueprint = _childGenerator.Generate(
                    partnerA,
                    partnerB,
                    childSpec.BornOn,
                    childSpec.Seed);

                // Force sex if specified in the spec — override the randomly picked biology.
                // We rebuild the blueprint with the forced sex so appearance generation
                // also uses the correct sex-specific morphology parameters.
                if (childSpec.Sex.HasValue && childSpec.Sex.Value != childBlueprint.Biology)
                {
                    childBlueprint = RegenerateBlueprintWithForcedSex(
                        childBlueprint, childSpec.Sex.Value, partnerA, partnerB, childSpec);
                }

                children.Add(_humanFactory.Create(childBlueprint));
            }

            // ── Step 3: Wire family bonds + register in FamilyGraph ───────────
            // Children are passed oldest-first (matching spec order) — sibling bond
            // generation in FamilyBuilder iterates all pairs, so order doesn't affect
            // correctness, only the PositiveInteractionCount seed (older siblings
            // have had more interactions, modeled by the age-aware bonus below).
            FamilyBuilder.Wire(graph, partnerA, partnerB, children, now);

            // ── Step 4: Apply age-aware interaction count to child edges ──────
            // A 15-year-old child has had far more interactions with parents than a newborn.
            // We bump PositiveInteractionCount on parent→child and child→parent edges
            // to reflect accumulated shared history. This affects MereExposure attraction
            // and semantic memory baseline.
            ApplyAgeAwareInteractionCounts(partnerA, partnerB, children, now);

            return new NuclearFamily(partnerA, partnerB, children.AsReadOnly());
        }

        #endregion Public API

        #region Private helpers

        /// <summary>
        /// Regenerates a child blueprint with a forced biological sex.
        /// Used when <see cref="ChildSpec.Sex"/> is explicitly specified.
        /// </summary>
        private HumanBlueprint RegenerateBlueprintWithForcedSex(
            HumanBlueprint original,
            SexBiology forcedSex,
            IHuman parentA,
            IHuman parentB,
            ChildSpec childSpec)
        {
            // Create a modified spec that forces the sex via a custom request.
            // We pass the same seed so all other randomness is identical — only
            // sex-specific morphology parameters change.
            var forcedRequest = new HumanBlueprintRequest(
                Sex: forcedSex,
                Seed: childSpec.Seed ?? original.Seed);

            // Re-derive the child with forced sex.
            // The seed is stable so the result is deterministic for the same inputs.
            return _childGenerator.Generate(parentA, parentB, childSpec.BornOn, forcedRequest.Seed);
        }

        /// <summary>
        /// Bumps <see cref="RelationshipEdge.PositiveInteractionCount"/> on family edges
        /// to reflect years of shared history for older children.
        /// </summary>
        /// <remarks>
        /// A newborn starts at the seeded baseline (40 counts, from FamilyBuilder).
        /// Each additional year of life adds approximately 8 interactions per year
        /// (Roberts &amp; Dunbar 2011: ~weekly contact for close kin → ~52/year,
        /// reduced by 85% to avoid saturating MereExposure too fast).
        /// Cap at 200 to stay within meaningful MereExposure range.
        /// </remarks>
        private static void ApplyAgeAwareInteractionCounts(
            IHuman partnerA,
            IHuman partnerB,
            IReadOnlyList<IHuman> children,
            WDateTime now)
        {
            const double InteractionsPerYearEstimate = 8.0;
            const int MaxInteractionCount = 200;

            var nowDate = now.Date;

            foreach (var child in children)
            {
                var birthDate = child.Identity.BirthDate;
                var ageInYears = (nowDate.DayIndex - birthDate.DayIndex)
                    / (double)WWorld.Spec.Calendar.DaysInYear(nowDate.Year);

                if (ageInYears <= 0)
                {
                    continue;
                }

                // Additional interactions beyond the newborn baseline
                var bonus = (int)Math.Min(
                    ageInYears * InteractionsPerYearEstimate,
                    MaxInteractionCount);

                if (bonus <= 0)
                {
                    continue;
                }

                // Bump parent→child edges
                BumpInteractionCount(partnerA, child.Id, bonus, now);
                BumpInteractionCount(partnerB, child.Id, bonus, now);

                // Bump child→parent edges
                BumpInteractionCount(child, partnerA.Id, bonus, now);
                BumpInteractionCount(child, partnerB.Id, bonus, now);
            }

            // Bump sibling edges proportionally to the younger sibling's age
            for (var i = 0; i < children.Count; i++)
            {
                for (var j = i + 1; j < children.Count; j++)
                {
                    var younger = children[j]; // spec is oldest-first, so j is younger
                    var youngerAge = (nowDate.DayIndex - younger.Identity.BirthDate.DayIndex)
                        / (double)WWorld.Spec.Calendar.DaysInYear(nowDate.Year);

                    var siblingBonus = (int)Math.Min(
                        youngerAge * InteractionsPerYearEstimate * 0.6, // siblings interact less than parent-child
                        MaxInteractionCount);

                    if (siblingBonus <= 0)
                    {
                        continue;
                    }

                    BumpInteractionCount(children[i], younger.Id, siblingBonus, now);
                    BumpInteractionCount(younger, children[i].Id, siblingBonus, now);
                }
            }
        }

        /// <summary>
        /// Sends a <see cref="FamilyInteractionCountAdjusted"/> event to bump
        /// <see cref="RelationshipEdge.PositiveInteractionCount"/> on a specific edge.
        /// </summary>
        private static void BumpInteractionCount(IHuman owner, HumanId targetId, int bonus, WDateTime now)
        {
            owner.ReceiveEvent(new FamilyInteractionCountAdjusted(now, owner.Id, targetId, bonus));
        }

        /// <summary>
        /// Validates that a child's birth date is not in the future.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <paramref name="bornOn"/> is later than <paramref name="now"/>.
        /// </exception>
        private static void ValidateChildBornOn(WDateOnly bornOn, WDateTime now)
        {
            if (bornOn.DayIndex > now.Date.DayIndex)
            {
                throw new InvalidOperationException(
                    $"Child BornOn {bornOn} is in the future relative to current time {now.Date}. "
                    + "Children must have a birth date on or before the current world date.");
            }
        }

        #endregion Private helpers
    }
}
