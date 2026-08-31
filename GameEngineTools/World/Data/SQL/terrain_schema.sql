-- terrain_schema.sql
-- Copyright (c) 50PSoftware
--
-- Schema for a DEDICATED terrain database — separate from the main world.db's Locations/
-- Connections/WorldObjects schema (schema.sql). Applied via WorldDatabaseSeeder.InitializeTerrainDatabase
-- to any SqliteWorldDatabase instance opened purely for heightmap storage (TerrainEditor's sibling
-- terrain.db, WorldObserver's TerrainMapService, TerraGen's CLI output).

-- ── Terrain Heightmap ─────────────────────────────────────────────────────────
-- Authored by the standalone TerrainEditor tool (paints elevation, derives contour lines) or
-- generated in batch by TerraGen. One row per named heightmap grid — 'default' for a single
-- hand-edited window, or "tile_{seed}_{lat}_{lon}"-style ids for TerraGen's batch tiles.
-- Data is a packed row-major array of 32-bit floats, Width*Height entries.

CREATE TABLE IF NOT EXISTS TerrainHeightmap (
    Id              TEXT    PRIMARY KEY,
    OriginX         REAL    NOT NULL,
    OriginY         REAL    NOT NULL,
    CellSizeMeters  REAL    NOT NULL,
    Width           INTEGER NOT NULL,
    Height          INTEGER NOT NULL,
    Data            BLOB    NOT NULL,
    RiverMask       BLOB            -- nullable: 0/1 byte per cell, painted separately from elevation
);
