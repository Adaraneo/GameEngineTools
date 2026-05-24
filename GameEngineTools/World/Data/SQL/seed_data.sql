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

INSERT OR IGNORE INTO Locations
    (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson,
     Capacity, AllowsPrivacy, Terrain, DangerLevel, AllowsPickup)
VALUES
    ('castle_hall',     'Castle Hall',      'Social',  'Castle',  0.20, 0.05, 20, 0, 'Indoor',    0.0, 1),
    ('library',         'Library',          'Private', 'Castle',  0.05, 0.02,  5, 1, 'Indoor',    0.0, 1),
    ('courtyard',       'Courtyard',        'Public',  'Castle',  0.30, 0.04, 30, 0, 'Courtyard', 0.0, 1),
    ('throne_room',     'Throne Room',      'Social',  'Castle',  0.10, 0.03, 15, 0, 'Indoor',    0.0, 1),
    ('stables',         'Stables',          'Work',    'Castle',  0.40, 0.06, 10, 0, 'Indoor',    0.0, 1),
    ('dungeon_entrance','Dungeon Entrance',  'Public',  'Castle',  0.05, 0.02, 10, 0, 'Indoor',    0.4, 0),
    ('dungeon_cell',    'Dungeon Cell',      'Private', 'Castle',  0.02, 0.01,  2, 1, 'Indoor',    0.6, 1),
    ('crypt',           'Ancient Crypt',     'Private', 'Castle',  0.01, 0.01,  4, 1, 'Indoor',    0.7, 0),
    ('tavern',          'Tavern',            'Social',  'Village', 0.50, 0.08, 25, 0, 'Indoor',    0.0, 1),
    ('market_square',   'Market Square',     'Public',  'Village', 0.60, 0.07, 50, 0, 'Courtyard', 0.0, 1),
    ('blacksmith',      'Blacksmith',        'Work',    'Village', 0.70, 0.05,  8, 0, 'Indoor',    0.0, 1),
    ('inn_room',        'Inn Room',          'Rest',    'Village', 0.05, 0.03,  3, 1, 'Indoor',    0.0, 1),
    ('chapel',          'Chapel',            'Private', 'Village', 0.05, 0.01, 20, 0, 'Indoor',    0.0, 1),
    ('herb_garden',     'Herb Garden',       'Work',    'Village', 0.05, 0.03,  8, 0, 'Courtyard', 0.0, 1),
    ('abandoned_mill',  'Abandoned Mill',    'Work',    'Village', 0.05, 0.03,  6, 0, 'Indoor',    0.2, 1),
    ('forest',          'Forest',            'Public',  'Forest',  0.05, 0.06,1000,1, 'Forest',    0.1, 1),
    ('forest_clearing', 'Forest Clearing',   'Public',  'Wilds',   0.10, 0.05, 20, 0, 'Forest',    0.2, 1),
    ('river_crossing',  'River Crossing',    'Public',  'Wilds',   0.15, 0.05, 10, 0, 'Water',     0.3, 0),
    ('mountain_pass',   'Mountain Pass',     'Public',  'Wilds',   0.05, 0.02,  8, 0, 'Mountain',  0.5, 0);

-- ── Connections ───────────────────────────────────────────────────────────────
-- Doplň podle skutečné topologie svého světa.
-- Toto jsou rozumné defaulty podle geografie výše.

INSERT OR IGNORE INTO Connections (FromId, ToId, DistanceMeters)
VALUES
    ('castle_hall',      'library',          50.0),
    ('library',          'castle_hall',      50.0),
    ('castle_hall',      'courtyard',        40.0),
    ('courtyard',        'castle_hall',      40.0),
    ('castle_hall',      'throne_room',      30.0),
    ('throne_room',      'castle_hall',      30.0),
    ('courtyard',        'stables',          60.0),
    ('stables',          'courtyard',        60.0),
    ('castle_hall',      'dungeon_entrance', 80.0),
    ('dungeon_entrance', 'castle_hall',      80.0),
    ('dungeon_entrance', 'dungeon_cell',     30.0),
    ('dungeon_cell',     'dungeon_entrance', 30.0),
    ('dungeon_entrance', 'crypt',            50.0),
    ('crypt',            'dungeon_entrance', 50.0),
    ('courtyard',        'tavern',          300.0),
    ('tavern',           'courtyard',       300.0),
    ('tavern',           'market_square',    80.0),
    ('market_square',    'tavern',           80.0),
    ('market_square',    'blacksmith',       40.0),
    ('blacksmith',       'market_square',    40.0),
    ('tavern',           'inn_room',         20.0),
    ('inn_room',         'tavern',           20.0),
    ('market_square',    'chapel',          100.0),
    ('chapel',           'market_square',   100.0),
    ('market_square',    'herb_garden',     120.0),
    ('herb_garden',      'market_square',   120.0),
    ('market_square',    'abandoned_mill',  200.0),
    ('abandoned_mill',   'market_square',   200.0),
    ('tavern',           'forest',          400.0),
    ('forest',           'tavern',          400.0),
    ('forest',           'forest_clearing', 150.0),
    ('forest_clearing',  'forest',          150.0),
    ('forest_clearing',  'river_crossing',  200.0),
    ('river_crossing',   'forest_clearing', 200.0),
    ('river_crossing',   'mountain_pass',   500.0),
    ('mountain_pass',    'river_crossing',  500.0);

-- ── World Objects ─────────────────────────────────────────────────────────────

 INSERT OR IGNORE INTO WorldObjects
     (Id, DisplayName, Category, LocationId,
      HeatSignature, AmbientNoise, BlocksLineOfSight,
      IsAvailable, IsPickable, WeightGrams, ItemKind,
      Respawns, RespawnMinutes, HeldBy)
 VALUES
     ('tavern_bread_01', 'Bread', 'Food', 'tavern',
      0.0, 0.0, 0,
      1, 1, 200, 'Food',
      1, 720, NULL),
     ('tavern_ale_01', 'Ale Mug', 'Drink', 'tavern',
      0.0, 0.0, 0,
      1, 1, 300, 'Food',
      1, 480, NULL),
     ('castle_hall_table_01', 'Long Table', 'Furniture', 'castle_hall',
      0.0, 0.0, 0,
      1, 0, 0, 'None',
      0, 0, NULL);

-- ── Affordances ───────────────────────────────────────────────────────────────

 INSERT OR IGNORE INTO Affordances (ObjectId, Type, Satisfaction)
 VALUES
     ('tavern_bread_01', 'Hunger',  0.6),
     ('tavern_ale_01',   'Hunger',  0.3),
     ('castle_hall_table_01', 'Social', 0.2);

-- ── Nutritional Profiles ──────────────────────────────────────────────────────

 INSERT OR IGNORE INTO NutritionalProfiles
     (ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain)
 VALUES
     ('tavern_bread_01', 55.0, 8.0,  NULL, NULL, NULL),
     ('tavern_ale_01',   20.0, NULL, NULL, NULL, 50.0);
