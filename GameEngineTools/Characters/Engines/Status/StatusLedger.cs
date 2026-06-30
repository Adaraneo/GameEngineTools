// StatusLedger.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Status
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Relationships;

    /// <summary>
    /// Scene-level aggregate that distils per-observer
    /// <see cref="RelationshipEdge.PerceivedDominance"/> / <see cref="RelationshipEdge.PerceivedPrestige"/>
    /// into a per-agent emergent <see cref="SocietalStatus"/> (a weighted consensus of the surrounding
    /// network) and tracks how stable the resulting hierarchy is over time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Analogous to <see cref="GameEngineTools.Characters.Engines.Reputation.CommunityReputationLedger"/>:
    /// it sits beside the per-observer relationship edges (which it never mutates) and answers
    /// network-level questions. Status is <b>emergent</b> — it is conferred by others, never a self-attribute
    /// (Anderson, Hildreth &amp; Howland 2015). The Dominance and Prestige axes are aggregated independently
    /// because they are orthogonal (Cheng et al. 2013).
    /// </para>
    /// <para>
    /// Registered as a scene singleton. <see cref="Fold"/> is called once per tick by the scene
    /// orchestrator; <see cref="Get"/> and <see cref="HierarchyStability"/> are read by the orchestrator
    /// (deference) and pushed into characters' snapshots (status×stability stress in Psychology).
    /// </para>
    /// </remarks>
    public sealed class StatusLedger
    {
        private readonly StatusConfig _config;
        private readonly IAscribedStatusProvider? _ascribed;

        private readonly Dictionary<HumanId, SocietalStatus> _status = new();
        private readonly Dictionary<HumanId, double> _prevSalience = new();

        // Reusable per-target observation buffer to avoid per-fold allocation.
        private readonly Dictionary<HumanId, List<(double Dominance, double Prestige, double Weight)>> _scratch = new();

        private double _stability = 1.0;

        /// <summary>Creates a ledger with the given tuning configuration (defaults when omitted).</summary>
        /// <param name="config">Tuning configuration.</param>
        /// <param name="ascribed">
        /// Optional ascribed-status provider. When supplied, <see cref="Get"/> blends each agent's role/
        /// occupation/lineage prior into the emergent consensus by <see cref="StatusConfig.AscribedPersistence"/>.
        /// </param>
        public StatusLedger(StatusConfig? config = null, IAscribedStatusProvider? ascribed = null)
        {
            _config = config ?? new StatusConfig();
            _ascribed = ascribed;
        }

        /// <summary>Tuning configuration in effect for this ledger.</summary>
        public StatusConfig Config => _config;

        /// <summary>
        /// Recomputes every agent's emergent status from the current relationship graph and updates the
        /// hierarchy-stability estimate. One entry per observer: their id and their outgoing edges.
        /// </summary>
        /// <param name="graph">
        /// The full directed relationship graph — for each observer, the edges describing how that
        /// observer perceives the people they know.
        /// </param>
        public void Fold(IEnumerable<(HumanId Observer, IReadOnlyDictionary<HumanId, RelationshipEdge> Edges)> graph)
        {
            foreach (var list in _scratch.Values)
                list.Clear();

            // Gather, per target, every qualifying observer's perception weighted by familiarity.
            foreach (var (_, edges) in graph)
            {
                foreach (var (targetId, edge) in edges)
                {
                    if (edge.Familiarity < _config.MinObserverFamiliarity)
                        continue;

                    if (!_scratch.TryGetValue(targetId, out var bucket))
                    {
                        bucket = new List<(double, double, double)>();
                        _scratch[targetId] = bucket;
                    }

                    var weight = _config.FamiliarityWeightFloor + edge.Familiarity / 100.0;
                    bucket.Add((edge.PerceivedDominance, edge.PerceivedPrestige, weight));
                }
            }

            // Compute consensus per target and accumulate salience churn vs. the previous fold.
            _status.Clear();
            var churnSum = 0.0;
            var churnCount = 0;

            foreach (var (targetId, bucket) in _scratch)
            {
                if (bucket.Count == 0)
                    continue;

                var consensus = StatusMath.Consensus(bucket);
                _status[targetId] = consensus;

                if (_prevSalience.TryGetValue(targetId, out var prev))
                {
                    churnSum += System.Math.Abs(consensus.Salience - prev);
                    churnCount++;
                }

                _prevSalience[targetId] = consensus.Salience;
            }

            if (churnCount > 0)
            {
                var instantStability = StatusMath.StabilityFromChurn(churnSum / churnCount, _config.StabilityChurnScale);
                // Exponential smoothing so a single reshuffle does not flip the hierarchy state.
                _stability += _config.StabilitySmoothing * (instantStability - _stability);
            }
        }

        /// <summary>
        /// The status of <paramref name="target"/>: the emergent consensus, blended with their ascribed
        /// prior (role/occupation/lineage) when one exists. Returns <see cref="SocietalStatus.Neutral"/>
        /// when there is neither a consensus nor an ascribed prior.
        /// </summary>
        public SocietalStatus Get(HumanId target)
        {
            var consensus = _status.TryGetValue(target, out var s) ? s : SocietalStatus.Neutral;

            if (_ascribed?.GetPrior(target) is { } prior)
                return StatusMath.BlendAscribed(consensus, prior, _config.AscribedPersistence);

            return consensus;
        }

        /// <summary>
        /// Whether <paramref name="target"/> currently has a non-default status — an emergent consensus
        /// or an ascribed prior.
        /// </summary>
        public bool Has(HumanId target) => _status.ContainsKey(target) || _ascribed?.GetPrior(target) is not null;

        /// <summary>The smoothed local hierarchy stability in [0,1] (1 = perfectly stable).</summary>
        public double HierarchyStability() => _stability;
    }
}
