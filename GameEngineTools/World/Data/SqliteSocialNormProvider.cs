// SqliteSocialNormProvider.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.Characters.Engines.Interactions;

    /// <summary>
    /// SQLite-backed <see cref="ISocialNormProvider"/>.
    /// Loads all norm rows at construction into an in-memory dictionary.
    /// Zero query latency during runtime — norms do not change after startup.
    /// </summary>
    public sealed class SqliteSocialNormProvider : ISocialNormProvider
    {
        private readonly IReadOnlyDictionary<string, SocialNormContext> _cache;

        /// <summary>
        /// Initialises the provider by loading all norms from the given database.
        /// </summary>
        public SqliteSocialNormProvider(SqliteWorldDatabase db)
        {
            ArgumentNullException.ThrowIfNull(db);

            _cache = db.GetAllSocialNorms()
                .ToDictionary(
                    r => r.Id,
                    r => new SocialNormContext(
                        Enum.Parse<SocialNormKind>(r.Kind, ignoreCase: true),
                        r.Severity,
                        r.EnforcementProbability,
                        r.RelationalModel is null
                            ? null
                            : Enum.Parse<RelationalModel>(r.RelationalModel, ignoreCase: true)));
        }

        /// <inheritdoc/>
        public SocialNormContext? GetNormContext(string normId)
            => _cache.TryGetValue(normId, out var ctx) ? ctx : null;
    }
}
