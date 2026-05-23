// WorldDatabaseSchema.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Data
{
    /// <summary>
    /// SQL DDL statements for the world SQLite database.
    /// Schema is created idempotently via IF NOT EXISTS guards.
    /// </summary>
    internal static class WorldDatabaseSchema
    {
        /// <summary>
        /// Full schema creation script. Safe to run on every startup —
        /// tables and indexes are only created when absent.
        /// </summary>
        internal const string CreateTables = """
            PRAGMA foreign_keys = ON;

            -- ── Locations ────────────────────────────────────────────────────
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
                AllowsPickup    INTEGER NOT NULL DEFAULT 1
            );

            -- ── Connections ───────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Connections (
                FromId          TEXT NOT NULL REFERENCES Locations(Id),
                ToId            TEXT NOT NULL REFERENCES Locations(Id),
                DistanceMeters  REAL NOT NULL,
                PRIMARY KEY (FromId, ToId)
            );

            -- ── World Objects ─────────────────────────────────────────────────
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
                HeldBy            TEXT,      -- NULL = not held; TEXT = holder HumanId GUID
                ConsumedAt        INTEGER    -- NULL = available; INTEGER = WDateTime ticks
            );

            -- ── Affordances ───────────────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS Affordances (
                ObjectId     TEXT NOT NULL REFERENCES WorldObjects(Id) ON DELETE CASCADE,
                Type         TEXT NOT NULL,
                Satisfaction REAL NOT NULL,
                PRIMARY KEY (ObjectId, Type)
            );

            -- ── Nutritional Profiles ──────────────────────────────────────────
            CREATE TABLE IF NOT EXISTS NutritionalProfiles (
                ObjectId      TEXT PRIMARY KEY REFERENCES WorldObjects(Id) ON DELETE CASCADE,
                CalorieGain   REAL,
                ProteinGain   REAL,
                IronGain      REAL,
                VitaminDGain  REAL,
                HydrationGain REAL
            );

            -- ── Indexes ───────────────────────────────────────────────────────
            CREATE INDEX IF NOT EXISTS idx_objects_location
                ON WorldObjects(LocationId);

            CREATE INDEX IF NOT EXISTS idx_objects_category_available
                ON WorldObjects(Category, IsAvailable)
                WHERE HeldBy IS NULL AND ConsumedAt IS NULL;

            CREATE INDEX IF NOT EXISTS idx_objects_heldby
                ON WorldObjects(HeldBy)
                WHERE HeldBy IS NOT NULL;

            CREATE INDEX IF NOT EXISTS idx_affordances_object
                ON Affordances(ObjectId);

            CREATE INDEX IF NOT EXISTS idx_locations_type
                ON Locations(Type);
            """;
    }
}
