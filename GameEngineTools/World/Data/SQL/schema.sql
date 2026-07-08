-- schema.sql
-- World database schema for GameEngineTools.
-- Run on every startup — all statements are idempotent (IF NOT EXISTS / INSERT OR IGNORE).
-- Copyright (c) 50PSoftware

PRAGMA foreign_keys = ON;

-- ── Schema version ────────────────────────────────────────────────────────────
-- Increment Version when the schema changes. Used by SqliteWorldDatabase
-- to detect whether a migration is needed.

CREATE TABLE IF NOT EXISTS SchemaVersion (
    Version   INTEGER NOT NULL,
    AppliedAt TEXT    NOT NULL  -- ISO-8601 UTC timestamp for diagnostics
);

-- ── Social Norms ──────────────────────────────────────────────────────────────
-- Defines social norm contexts attachable to locations.
-- Kind must match a valid SocialNormKind enum value (TEXT, case-insensitive parse).
-- RelationalModel is optional — NULL means no relational model override.
-- CultureId and ValidFromYear/ValidToYear are reserved for future cultural evolution.

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

-- ── Locations ─────────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS Locations (
    Id              TEXT    PRIMARY KEY,
    DisplayName     TEXT    NOT NULL,
    Type            TEXT    NOT NULL,
    Region          TEXT    NOT NULL DEFAULT '',
    BaseNoise       REAL    NOT NULL DEFAULT 0.1,
    NoisePerPerson  REAL    NOT NULL DEFAULT 0.02,
    Capacity        INTEGER NOT NULL DEFAULT 20,
    AllowsPrivacy   INTEGER NOT NULL DEFAULT 0,  -- 0 = false, 1 = true
    Terrain         TEXT    NOT NULL DEFAULT 'Indoor',
    DangerLevel     REAL    NOT NULL DEFAULT 0.0,
    AllowsPickup    INTEGER NOT NULL DEFAULT 1,  -- 0 = false, 1 = true
    NormId          TEXT    REFERENCES SocialNorms(Id)  -- nullable
);

-- ── Connections ───────────────────────────────────────────────────────────────
-- Directed adjacency graph. For a bidirectional edge, declare both directions.

CREATE TABLE IF NOT EXISTS Connections (
    FromId          TEXT NOT NULL REFERENCES Locations(Id),
    ToId            TEXT NOT NULL REFERENCES Locations(Id),
    DistanceMeters  REAL NOT NULL,
    PRIMARY KEY (FromId, ToId)
);

-- ── World Objects ─────────────────────────────────────────────────────────────
-- HeldBy     NULL = object is at its location; TEXT = holder HumanId GUID.
-- ConsumedAt NULL = available; INTEGER = WDateTime.WorldTicks of consumption.
-- Price      NULL = free/foraged; REAL = shop price (food-economy Tier 2).
-- ShopId     NULL when Price is NULL; TEXT = owning shop id (Tier 2).

CREATE TABLE IF NOT EXISTS WorldObjects (
    Id                TEXT    PRIMARY KEY,
    DisplayName       TEXT    NOT NULL,
    Category          TEXT    NOT NULL,
    LocationId        TEXT    NOT NULL REFERENCES Locations(Id),
    HeatSignature     REAL    NOT NULL DEFAULT 0.0,
    AmbientNoise      REAL    NOT NULL DEFAULT 0.0,
    BlocksLineOfSight INTEGER NOT NULL DEFAULT 0,
    IsAvailable       INTEGER NOT NULL DEFAULT 1,
    IsPickable        INTEGER NOT NULL DEFAULT 0,
    WeightGrams       INTEGER NOT NULL DEFAULT 0,
    ItemKind          TEXT    NOT NULL DEFAULT 'None',
    Respawns          INTEGER NOT NULL DEFAULT 0,
    RespawnMinutes    INTEGER NOT NULL DEFAULT 1440,
    Price             REAL            DEFAULT NULL,
    ShopId            TEXT            DEFAULT NULL,
    HeldBy            TEXT            DEFAULT NULL,
    ConsumedAt        INTEGER         DEFAULT NULL
);

-- ── Affordances ───────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS Affordances (
    ObjectId     TEXT NOT NULL REFERENCES WorldObjects(Id) ON DELETE CASCADE,
    Type         TEXT NOT NULL,
    Satisfaction REAL NOT NULL,
    PRIMARY KEY (ObjectId, Type)
);

-- ── Nutritional Profiles ──────────────────────────────────────────────────────
-- All gain columns are nullable: NULL means "use engine config default".

CREATE TABLE IF NOT EXISTS NutritionalProfiles (
    ObjectId          TEXT PRIMARY KEY REFERENCES WorldObjects(Id) ON DELETE CASCADE,
    CalorieGain       REAL DEFAULT NULL,
    ProteinGain       REAL DEFAULT NULL,
    IronGain          REAL DEFAULT NULL,
    VitaminDGain      REAL DEFAULT NULL,
    HydrationGain     REAL DEFAULT NULL,
    HemeIronFraction  REAL DEFAULT NULL,
    VitaminCMilligrams REAL DEFAULT NULL
);

-- ── Indexes ───────────────────────────────────────────────────────────────────

-- Hot path: GetObjectsAt — called every behavior tick.
-- Partial index: only indexes available objects (not held, not consumed).
CREATE INDEX IF NOT EXISTS idx_objects_location_available
    ON WorldObjects(LocationId)
    WHERE HeldBy IS NULL AND ConsumedAt IS NULL;

-- Foraging queries (ContingencySearchEngine): find locations with Food/Drink.
CREATE INDEX IF NOT EXISTS idx_objects_category_available
    ON WorldObjects(Category, IsAvailable)
    WHERE HeldBy IS NULL AND ConsumedAt IS NULL;

-- Inventory queries (GetHeldBy).
CREATE INDEX IF NOT EXISTS idx_objects_heldby
    ON WorldObjects(HeldBy)
    WHERE HeldBy IS NOT NULL;

-- Respawn scheduler: find consumed objects awaiting respawn.
CREATE INDEX IF NOT EXISTS idx_objects_consumed
    ON WorldObjects(ConsumedAt)
    WHERE ConsumedAt IS NOT NULL;

-- Batch affordance loading (EnrichWithAffordances).
CREATE INDEX IF NOT EXISTS idx_affordances_object
    ON Affordances(ObjectId);

-- Location type filter (WorldMap / FindLocationWithCategory).
CREATE INDEX IF NOT EXISTS idx_locations_type
    ON Locations(Type);

-- ── Schema version stamp ──────────────────────────────────────────────────────
-- Inserted once. Subsequent runs hit the OR IGNORE guard.

INSERT OR IGNORE INTO SchemaVersion (Version, AppliedAt)
VALUES (1, datetime('now'));
