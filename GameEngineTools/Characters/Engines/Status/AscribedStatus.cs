// AscribedStatus.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Status
{
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;

    /// <summary>
    /// An ascribed social role — status conferred by birth, office or seniority rather than earned through
    /// interaction. Feeds a per-agent <see cref="SocietalStatus"/> prior into the <see cref="StatusLedger"/>.
    /// </summary>
    /// <remarks>
    /// Ascribed status is a <b>society parameter, not a frozen caste</b>: it shifts where a character starts
    /// on the ladder, but the emergent consensus still moves them (Corak 2013; Clark 2014 — intergenerational
    /// persistence ~0.4 "modern" ↔ ~0.75 "traditional", never 1.0). The persistence weight lives in
    /// <see cref="StatusConfig.AscribedPersistence"/>.
    /// </remarks>
    public enum AscribedRole
    {
        /// <summary>Ordinary member (pracovník) — no ascribed advantage (neutral prior).</summary>
        Commoner,

        /// <summary>A supervisory office holder (vedoucí) — modest ascribed standing.</summary>
        Official,

        /// <summary>A community leader (radní) — high freely-conferred prestige plus some authority.</summary>
        Leader,

        /// <summary>A respected elder — high prestige, low coercive dominance.</summary>
        Elder
    }

    /// <summary>
    /// Supplies a per-agent ascribed <see cref="SocietalStatus"/> prior (from role / occupation / lineage)
    /// that <see cref="StatusLedger"/> blends with the emergent consensus.
    /// </summary>
    public interface IAscribedStatusProvider
    {
        /// <summary>The ascribed status prior for <paramref name="id"/>, or <c>null</c> when none is assigned.</summary>
        SocietalStatus? GetPrior(HumanId id);
    }

    /// <summary>
    /// Default mutable <see cref="IAscribedStatusProvider"/>: consumers assign each character a role (or an
    /// explicit prior) — typically derived from their occupation or family — and the configured role priors
    /// (<see cref="StatusConfig"/>) are returned to the ledger.
    /// </summary>
    public sealed class DefaultAscribedStatusProvider : IAscribedStatusProvider
    {
        private readonly StatusConfig _config;
        private readonly Dictionary<HumanId, SocietalStatus> _priors = new();

        /// <summary>Creates a provider using the given role-prior configuration (defaults when omitted).</summary>
        public DefaultAscribedStatusProvider(StatusConfig? config = null) => _config = config ?? new StatusConfig();

        /// <summary>Assigns an ascribed <paramref name="role"/> to <paramref name="id"/> (mapped to a prior via config).</summary>
        public void SetRole(HumanId id, AscribedRole role)
        {
            var prior = _config.PriorForRole(role);
            if (prior is { } p)
                _priors[id] = p;
            else
                _priors.Remove(id); // Commoner → no ascribed advantage
        }

        /// <summary>Assigns an explicit ascribed prior to <paramref name="id"/>.</summary>
        public void SetPrior(HumanId id, SocietalStatus prior) => _priors[id] = prior;

        /// <summary>Clears any ascribed prior for <paramref name="id"/>.</summary>
        public void Clear(HumanId id) => _priors.Remove(id);

        /// <inheritdoc/>
        public SocietalStatus? GetPrior(HumanId id) => _priors.TryGetValue(id, out var p) ? p : null;
    }
}
