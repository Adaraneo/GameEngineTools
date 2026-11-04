// WorldDatabaseSchema.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    /// <summary>
    /// The world database schema. There is a single source of truth, the embedded <c>schema.sql</c>;
    /// this class only exposes it (it used to carry a second, drifting copy of the DDL).
    /// </summary>
    internal static class WorldDatabaseSchema
    {
        /// <summary>The full, idempotent schema script (<c>CREATE TABLE IF NOT EXISTS …</c>).</summary>
        internal static string CreateTables => SqlScriptLoader.Load("schema.sql");
    }
}
