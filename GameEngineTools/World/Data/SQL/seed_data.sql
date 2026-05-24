-- seed_data.sql
-- Default world data for GameEngineTools.
-- Executed once when the database is empty (no rows in Locations).
-- All statements use INSERT OR IGNORE — safe to re-run.
-- Copyright (c) 50PSoftware
--
-- HOW TO FILL THIS FILE:
--   Option A (recommended): Run WorldDatabaseExporter.ExportSeedSql(db)
--                           against your existing world.db to generate
--                           INSERT statements automatically.
--   Option B:               Write INSERT statements manually following
--                           the format shown in the examples below.
--
-- COLUMN ORDER:
--   Locations:  Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson,
--               Capacity, AllowsPrivacy, Terrain, DangerLevel, AllowsPickup
--   Connections: FromId, ToId, DistanceMeters
--   WorldObjects: Id, DisplayName, Category, LocationId, HeatSignature,
--                 AmbientNoise, BlocksLineOfSight, IsAvailable, IsPickable,
--                 WeightGrams, ItemKind, Respawns, RespawnMinutes, HeldBy
--   Affordances: ObjectId, Type, Satisfaction
--   NutritionalProfiles: ObjectId, CalorieGain, ProteinGain, IronGain,
--                        VitaminDGain, HydrationGain

-- ── Locations ─────────────────────────────────────────────────────────────────

-- INSERT OR IGNORE INTO Locations
--     (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson,
--      Capacity, AllowsPrivacy, Terrain, DangerLevel, AllowsPickup)
-- VALUES
--     ('tavern', 'The Rusty Flagon', 'Public', 'Village',
--      0.6, 0.05, 30, 0, 'Indoor', 0.1, 1),
--     ('castle_hall', 'Great Hall', 'Public', 'Castle',
--      0.3, 0.02, 50, 0, 'Indoor', 0.0, 1),
--     ('dungeon_cell', 'Dungeon Cell', 'Private', 'Castle',
--      0.05, 0.01, 4, 1, 'Underground', 0.6, 0);

-- ── Connections ───────────────────────────────────────────────────────────────

-- INSERT OR IGNORE INTO Connections (FromId, ToId, DistanceMeters)
-- VALUES
--     ('tavern',       'castle_hall',  250.0),
--     ('castle_hall',  'tavern',       250.0),
--     ('castle_hall',  'dungeon_cell', 80.0),
--     ('dungeon_cell', 'castle_hall',  80.0);

-- ── World Objects ─────────────────────────────────────────────────────────────

-- INSERT OR IGNORE INTO WorldObjects
--     (Id, DisplayName, Category, LocationId,
--      HeatSignature, AmbientNoise, BlocksLineOfSight,
--      IsAvailable, IsPickable, WeightGrams, ItemKind,
--      Respawns, RespawnMinutes, HeldBy)
-- VALUES
--     ('tavern_bread_01', 'Bread', 'Food', 'tavern',
--      0.0, 0.0, 0,
--      1, 1, 200, 'Food',
--      1, 720, NULL),
--     ('tavern_ale_01', 'Ale Mug', 'Drink', 'tavern',
--      0.0, 0.0, 0,
--      1, 1, 300, 'Food',
--      1, 480, NULL),
--     ('castle_hall_table_01', 'Long Table', 'Furniture', 'castle_hall',
--      0.0, 0.0, 0,
--      1, 0, 0, 'None',
--      0, 0, NULL);

-- ── Affordances ───────────────────────────────────────────────────────────────

-- INSERT OR IGNORE INTO Affordances (ObjectId, Type, Satisfaction)
-- VALUES
--     ('tavern_bread_01', 'Hunger',  0.6),
--     ('tavern_ale_01',   'Hunger',  0.3),
--     ('castle_hall_table_01', 'Social', 0.2);

-- ── Nutritional Profiles ──────────────────────────────────────────────────────

-- INSERT OR IGNORE INTO NutritionalProfiles
--     (ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain)
-- VALUES
--     ('tavern_bread_01', 55.0, 8.0,  NULL, NULL, NULL),
--     ('tavern_ale_01',   20.0, NULL, NULL, NULL, 50.0);
