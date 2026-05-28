// WorldDatabaseSchema.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    /// <summary>
    /// SQL DDL statements for the world SQLite database.
    /// All statements use IF NOT EXISTS guards and are safe to execute on every startup.
    /// </summary>
    internal static class WorldDatabaseSchema
    {
        /// <summary>
        /// Full schema creation script — idempotent, run on every application start.
        /// </summary>
        internal const string CreateTables = """
            PRAGMA foreign_keys = ON;

            -- ── Social Norms ──────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS SocialNorms (
                Id                      TEXT    PRIMARY KEY,
                DisplayName             TEXT    NOT NULL,
                Kind                    TEXT    NOT NULL,
                Severity                REAL    NOT NULL CHECK (Severity BETWEEN 0.0 AND 1.0),
                EnforcementProbability  REAL    NOT NULL CHECK (EnforcementProbability BETWEEN 0.0 AND 1.0),
                RelationalModel         TEXT,
                CultureId               TEXT,
                ValidFromYear           INTEGER,
                ValidToYear             INTEGER
            );

            -- ── Locations ─────────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Locations (
                Id              TEXT PRIMARY KEY,
                DisplayName     TEXT NOT NULL,
                Type            TEXT NOT NULL,
                Region          TEXT NOT NULL DEFAULT '',
                BaseNoise       REAL NOT NULL DEFAULT 0.1,
                NoisePerPerson  REAL NOT NULL DEFAULT 0.02,
                Capacity        INTEGER NOT NULL DEFAULT 20,
                AllowsPrivacy   INTEGER NOT NULL DEFAULT 0,
                Terrain         TEXT NOT NULL DEFAULT 'Indoor',
                DangerLevel     REAL NOT NULL DEFAULT 0.0,
                AllowsPickup    INTEGER NOT NULL DEFAULT 1,
                NormId          TEXT    REFERENCES SocialNorms(Id)
            );

            -- ── Connections ───────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Connections (
                FromId          TEXT NOT NULL REFERENCES Locations(Id),
                ToId            TEXT NOT NULL REFERENCES Locations(Id),
                DistanceMeters  REAL NOT NULL,
                PRIMARY KEY (FromId, ToId)
            );

            -- ── World Objects ─────────────────────────────────────────────────────
            -- HeldBy     NULL = not held; TEXT = holder HumanId GUID.
            -- ConsumedAt NULL = available; INTEGER = WDateTime ticks of consumption.
            CREATE TABLE IF NOT EXISTS WorldObjects (
                Id                TEXT PRIMARY KEY,
                DisplayName       TEXT NOT NULL,
                Category          TEXT NOT NULL,
                LocationId        TEXT NOT NULL REFERENCES Locations(Id),
                HeatSignature     REAL NOT NULL DEFAULT 0.0,
                AmbientNoise      REAL NOT NULL DEFAULT 0.0,
                BlocksLineOfSight INTEGER NOT NULL DEFAULT 0,
                IsAvailable       INTEGER NOT NULL DEFAULT 1,
                IsPickable        INTEGER NOT NULL DEFAULT 0,
                WeightGrams       INTEGER NOT NULL DEFAULT 0,
                ItemKind          TEXT NOT NULL DEFAULT 'None',
                Respawns          INTEGER NOT NULL DEFAULT 0,
                RespawnMinutes    INTEGER NOT NULL DEFAULT 1440,
                HeldBy            TEXT    DEFAULT NULL,
                ConsumedAt        INTEGER DEFAULT NULL
            );

            -- ── Affordances ───────────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Affordances (
                ObjectId     TEXT NOT NULL REFERENCES WorldObjects(Id) ON DELETE CASCADE,
                Type         TEXT NOT NULL,
                Satisfaction REAL NOT NULL,
                PRIMARY KEY (ObjectId, Type)
            );

            -- ── Nutritional Profiles ──────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS NutritionalProfiles (
                ObjectId      TEXT PRIMARY KEY REFERENCES WorldObjects(Id) ON DELETE CASCADE,
                CalorieGain   REAL DEFAULT NULL,
                ProteinGain   REAL DEFAULT NULL,
                IronGain      REAL DEFAULT NULL,
                VitaminDGain  REAL DEFAULT NULL,
                HydrationGain REAL DEFAULT NULL
            );

            -- ── Indexes ───────────────────────────────────────────────────────────

            -- Fast lookup of available objects at a given location (hot path — every tick).
            CREATE INDEX IF NOT EXISTS idx_objects_location_available
                ON WorldObjects(LocationId)
                WHERE HeldBy IS NULL AND ConsumedAt IS NULL;

            -- Category + availability filter for foraging queries (ContingencySearchEngine).
            CREATE INDEX IF NOT EXISTS idx_objects_category_available
                ON WorldObjects(Category, IsAvailable)
                WHERE HeldBy IS NULL AND ConsumedAt IS NULL;

            -- Held-by lookup for inventory queries.
            CREATE INDEX IF NOT EXISTS idx_objects_heldby
                ON WorldObjects(HeldBy)
                WHERE HeldBy IS NOT NULL;

            -- Consumed objects lookup for ObjectRespawnScheduler.
            CREATE INDEX IF NOT EXISTS idx_objects_consumed
                ON WorldObjects(ConsumedAt)
                WHERE ConsumedAt IS NOT NULL;

            -- Affordances by object (batch-loaded during EnrichWithAffordances).
            CREATE INDEX IF NOT EXISTS idx_affordances_object
                ON Affordances(ObjectId);

            -- Location type filter for WorldMap queries.
            CREATE INDEX IF NOT EXISTS idx_locations_type
                ON Locations(Type);
            """;
    }
}
