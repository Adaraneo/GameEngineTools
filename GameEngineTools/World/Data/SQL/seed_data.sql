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

-- ══════════════════════════════════════════════════════════════════════════════
-- WORLD OBJECTS
-- Column order: Id, DisplayName, Category, LocationId,
--               HeatSignature, AmbientNoise, BlocksLineOfSight,
--               IsAvailable, IsPickable, WeightGrams, ItemKind,
--               Respawns, RespawnMinutes, HeldBy
-- ══════════════════════════════════════════════════════════════════════════════
 
INSERT OR IGNORE INTO WorldObjects
    (Id, DisplayName, Category, LocationId,
     HeatSignature, AmbientNoise, BlocksLineOfSight,
     IsAvailable, IsPickable, WeightGrams, ItemKind,
     Respawns, RespawnMinutes, HeldBy)
VALUES
 
    -- ── TAVERN ────────────────────────────────────────────────────────────────
    -- Medieval tavern: hearty prepared food, ale, pottage.
    -- High calorie, moderate protein — innkeeper restocks twice daily.
 
    ('tavern_pottage_01', 'Pottage Bowl',    'Food', 'tavern', 0.3, 0.0, 0, 1, 1, 400, 'Food', 1, 360, NULL),
    ('tavern_pottage_02', 'Pottage Bowl',    'Food', 'tavern', 0.3, 0.0, 0, 1, 1, 400, 'Food', 1, 360, NULL),
    ('tavern_pottage_03', 'Pottage Bowl',    'Food', 'tavern', 0.3, 0.0, 0, 1, 1, 400, 'Food', 1, 360, NULL),
    ('tavern_roast_01',   'Roasted Chicken', 'Food', 'tavern', 0.4, 0.0, 0, 1, 1, 350, 'Food', 1, 480, NULL),
    ('tavern_roast_02',   'Roasted Chicken', 'Food', 'tavern', 0.4, 0.0, 0, 1, 1, 350, 'Food', 1, 480, NULL),
    ('tavern_cheese_01',  'Aged Cheese',     'Food', 'tavern', 0.0, 0.0, 0, 1, 1, 120, 'Food', 1, 720, NULL),
    ('tavern_water_01',   'Water Jug',       'Drink','tavern', 0.0, 0.0, 0, 1, 1, 500, 'Food', 1, 120, NULL),
    ('tavern_water_02',   'Water Jug',       'Drink','tavern', 0.0, 0.0, 0, 1, 1, 500, 'Food', 1, 120, NULL),
    ('tavern_ale_01', 'Ale Mug', 'Drink', 'tavern', 0.0, 0.0, 0, 1, 1, 300, 'Food', 1, 480, NULL),
 
    -- ── CASTLE HALL ───────────────────────────────────────────────────────────
    -- Noble household: salted meat, preserved fish, fine bread, wine.
    -- Higher protein and iron — nobles ate better than peasants.
 
    ('castle_salted_meat_01', 'Salted Pork',     'Food', 'castle_hall', 0.0, 0.0, 0, 1, 1, 300, 'Food', 1, 480, NULL),
    ('castle_salted_meat_02', 'Salted Pork',     'Food', 'castle_hall', 0.0, 0.0, 0, 1, 1, 300, 'Food', 1, 480, NULL),
    ('castle_dried_fish_01',  'Dried Herring',   'Food', 'castle_hall', 0.0, 0.0, 0, 1, 1, 150, 'Food', 1, 720, NULL),
    ('castle_dried_fish_02',  'Dried Herring',   'Food', 'castle_hall', 0.0, 0.0, 0, 1, 1, 150, 'Food', 1, 720, NULL),
    ('castle_fine_bread_01',  'Fine White Bread','Food', 'castle_hall', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 480, NULL),
    ('castle_wine_01',        'Wine Cup',        'Drink','castle_hall', 0.0, 0.0, 0, 1, 1, 250, 'Food', 1, 360, NULL),
    ('castle_wine_02',        'Wine Cup',        'Drink','castle_hall', 0.0, 0.0, 0, 1, 1, 250, 'Food', 1, 360, NULL),
 
    -- ── COURTYARD ─────────────────────────────────────────────────────────────
    -- Servants and guards eat here: simple dark bread, water, occasional apple.
    -- Lower calories, lower protein — working-class medieval diet.
 
    ('courtyard_dark_bread_01', 'Dark Rye Bread', 'Food', 'courtyard', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 480, NULL),
    ('courtyard_dark_bread_02', 'Dark Rye Bread', 'Food', 'courtyard', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 480, NULL),
    ('courtyard_apple_01',      'Apple',          'Food', 'courtyard', 0.0, 0.0, 0, 1, 1,  80, 'Food', 1, 720, NULL),
    ('courtyard_apple_02',      'Apple',          'Food', 'courtyard', 0.0, 0.0, 0, 1, 1,  80, 'Food', 1, 720, NULL),
    ('courtyard_water_01',      'Well Water',     'Drink','courtyard', 0.0, 0.0, 0, 1, 1, 500, 'Food', 1,  60, NULL),
    ('courtyard_water_02',      'Well Water',     'Drink','courtyard', 0.0, 0.0, 0, 1, 1, 500, 'Food', 1,  60, NULL),
 
    -- ── STABLES ───────────────────────────────────────────────────────────────
    -- Stable hands eat very simply. Mostly bread, water, sometimes cheese.
 
    ('stables_bread_01',  'Coarse Bread', 'Food', 'stables', 0.0, 0.0, 0, 1, 1, 180, 'Food', 1, 720, NULL),
    ('stables_water_01',  'Trough Water', 'Drink','stables', 0.0, 0.0, 0, 1, 0, 500, 'Food', 1,  60, NULL),
 
    -- ── HERB GARDEN ───────────────────────────────────────────────────────────
    -- Vitamin and mineral rich: fresh vegetables, edible herbs, legumes.
    -- Best location for iron and vitamins — critical for female NPCs.
    -- Outdoor location → VitaminD via sun exposure (IrradianceFactor).
 
    ('herb_garden_kale_01',    'Wild Kale',     'Food', 'herb_garden', 0.0, 0.0, 0, 1, 1, 100, 'Food', 1, 1440, NULL),
    ('herb_garden_kale_02',    'Wild Kale',     'Food', 'herb_garden', 0.0, 0.0, 0, 1, 1, 100, 'Food', 1, 1440, NULL),
    ('herb_garden_legumes_01', 'Lentils',       'Food', 'herb_garden', 0.0, 0.0, 0, 1, 1, 150, 'Food', 1, 1440, NULL),
    ('herb_garden_legumes_02', 'Lentils',       'Food', 'herb_garden', 0.0, 0.0, 0, 1, 1, 150, 'Food', 1, 1440, NULL),
    ('herb_garden_herbs_01',   'Healing Herbs', 'Food', 'herb_garden', 0.0, 0.0, 0, 1, 1,  30, 'Food', 1,  720, NULL),
    ('herb_garden_water_01',   'Rain Barrel',   'Drink','herb_garden', 0.0, 0.0, 0, 1, 0, 500, 'Food', 1,  120, NULL),
 
    -- ── MARKET SQUARE ─────────────────────────────────────────────────────────
    -- Variety of goods. Market rhythms: restocks every 480–720 min.
    -- Available during day only in a real simulation (not modeled here yet).
 
    ('market_bread_01',   'Peasant Bread', 'Food', 'market_square', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 480, NULL),
    ('market_bread_02',   'Peasant Bread', 'Food', 'market_square', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 480, NULL),
    ('market_cheese_01',  'Fresh Cheese',  'Food', 'market_square', 0.0, 0.0, 0, 1, 1, 120, 'Food', 1, 720, NULL),
    ('market_dried_01',   'Dried Peas',    'Food', 'market_square', 0.0, 0.0, 0, 1, 1, 200, 'Food', 1, 720, NULL),
    ('market_apple_01',   'Market Apple',  'Food', 'market_square', 0.0, 0.0, 0, 1, 1,  90, 'Food', 1, 480, NULL),
    ('market_apple_02',   'Market Apple',  'Food', 'market_square', 0.0, 0.0, 0, 1, 1,  90, 'Food', 1, 480, NULL),
    ('market_water_01',   'Water Barrel',  'Drink','market_square', 0.0, 0.0, 0, 1, 0, 500, 'Food', 1,  60, NULL),
 
    -- ── FOREST ────────────────────────────────────────────────────────────────
    -- Wild forage: mushrooms (VitaminD!), berries, edible plants.
    -- Mushrooms are the key winter VitaminD source when sun is weak.
    -- Low calorie but nutritionally diverse.
 
    ('forest_mushrooms_01', 'Wild Mushrooms', 'Food', 'forest', 0.0, 0.0, 0, 1, 1,  80, 'Food', 1,  720, NULL),
    ('forest_mushrooms_02', 'Wild Mushrooms', 'Food', 'forest', 0.0, 0.0, 0, 1, 1,  80, 'Food', 1,  720, NULL),
    ('forest_mushrooms_03', 'Wild Mushrooms', 'Food', 'forest', 0.0, 0.0, 0, 1, 1,  80, 'Food', 1,  720, NULL),
    ('forest_berries_01',   'Elderberries',   'Food', 'forest', 0.0, 0.0, 0, 1, 1,  60, 'Food', 1, 1440, NULL),
    ('forest_berries_02',   'Elderberries',   'Food', 'forest', 0.0, 0.0, 0, 1, 1,  60, 'Food', 1, 1440, NULL),
    ('forest_nuts_01',      'Hazelnuts',      'Food', 'forest', 0.0, 0.0, 0, 1, 1, 100, 'Food', 1, 1440, NULL),
    ('forest_stream_01',    'Forest Stream',  'Drink','forest', 0.0, 0.1, 0, 1, 0, 500, 'Food', 1,  120, NULL),
 
    -- ── FOREST CLEARING ───────────────────────────────────────────────────────
    -- Campsite foraging: similar to forest but slightly less dense.
 
    ('clearing_mushrooms_01', 'Forest Mushrooms','Food', 'forest_clearing', 0.0, 0.0, 0, 1, 1,  70, 'Food', 1,  720, NULL),
    ('clearing_berries_01',   'Wild Berries',    'Food', 'forest_clearing', 0.0, 0.0, 0, 1, 1,  50, 'Food', 1, 1440, NULL),
    ('clearing_nuts_01',      'Wild Nuts',       'Food', 'forest_clearing', 0.0, 0.0, 0, 1, 1,  90, 'Food', 1, 1440, NULL),
    ('clearing_water_01',     'Creek Water',     'Drink','forest_clearing', 0.0, 0.1, 0, 1, 0, 500, 'Food', 1,  120, NULL),
 
    -- ── RIVER CROSSING ────────────────────────────────────────────────────────
    -- Fish! Best protein and VitaminD source outside castle.
    -- River replenishes faster — shorter respawn than forest forage.
 
    ('river_fish_01',    'Fresh River Fish', 'Food', 'river_crossing', 0.1, 0.1, 0, 1, 1, 200, 'Food', 1, 360, NULL),
    ('river_fish_02',    'Fresh River Fish', 'Food', 'river_crossing', 0.1, 0.1, 0, 1, 1, 200, 'Food', 1, 360, NULL),
    ('river_fish_03',    'Fresh River Fish', 'Food', 'river_crossing', 0.1, 0.1, 0, 1, 1, 200, 'Food', 1, 360, NULL),
    ('river_cress_01',   'Watercress',       'Food', 'river_crossing', 0.0, 0.0, 0, 1, 1,  50, 'Food', 1, 720, NULL),
    ('river_water_01',   'River Water',      'Drink','river_crossing', 0.0, 0.1, 0, 1, 0, 500, 'Food', 1,  30, NULL),
    ('river_water_02',   'River Water',      'Drink','river_crossing', 0.0, 0.1, 0, 1, 0, 500, 'Food', 1,  30, NULL),
 
    -- ── MOUNTAIN PASS ─────────────────────────────────────────────────────────
    -- Harsh terrain: only dried/preserved rations a traveller might carry.
    -- Low variety, slow respawn (no kitchen resupply here).
 
    ('mountain_jerky_01',  'Dried Venison',  'Food', 'mountain_pass', 0.0, 0.0, 0, 1, 1, 120, 'Food', 1, 1440, NULL),
    ('mountain_jerky_02',  'Dried Venison',  'Food', 'mountain_pass', 0.0, 0.0, 0, 1, 1, 120, 'Food', 1, 1440, NULL),
    ('mountain_berries_01','Mountain Berries','Food','mountain_pass', 0.0, 0.0, 0, 1, 1,  40, 'Food', 1, 1440, NULL),
    ('mountain_snow_01',   'Melted Snow',    'Drink','mountain_pass', 0.0, 0.0, 0, 1, 0, 500, 'Food', 1,  60, NULL);
 
-- ══════════════════════════════════════════════════════════════════════════════
-- AFFORDANCES
-- Column order: ObjectId, Type, Satisfaction [0..1]
-- Satisfaction represents how much of the need this item satisfies per serving.
-- ══════════════════════════════════════════════════════════════════════════════
 
INSERT OR IGNORE INTO Affordances (ObjectId, Type, Satisfaction)
VALUES
    -- Tavern prepared food
    ('tavern_pottage_01', 'Hunger', 0.65),
    ('tavern_pottage_02', 'Hunger', 0.65),
    ('tavern_pottage_03', 'Hunger', 0.65),
    ('tavern_roast_01',   'Hunger', 0.80),
    ('tavern_roast_02',   'Hunger', 0.80),
    ('tavern_cheese_01',  'Hunger', 0.40),
 
    -- Castle food
    ('castle_salted_meat_01', 'Hunger', 0.75),
    ('castle_salted_meat_02', 'Hunger', 0.75),
    ('castle_dried_fish_01',  'Hunger', 0.60),
    ('castle_dried_fish_02',  'Hunger', 0.60),
    ('castle_fine_bread_01',  'Hunger', 0.55),
    ('castle_wine_01',        'Hunger', 0.15), -- wine: minor hunger reduction
    ('castle_wine_02',        'Hunger', 0.15),
 
    -- Courtyard
    ('courtyard_dark_bread_01', 'Hunger', 0.50),
    ('courtyard_dark_bread_02', 'Hunger', 0.50),
    ('courtyard_apple_01',      'Hunger', 0.25),
    ('courtyard_apple_02',      'Hunger', 0.25),
 
    -- Stables
    ('stables_bread_01', 'Hunger', 0.45),
 
    -- Herb garden — moderate satiety, high nutrition
    ('herb_garden_kale_01',    'Hunger', 0.30),
    ('herb_garden_kale_02',    'Hunger', 0.30),
    ('herb_garden_legumes_01', 'Hunger', 0.55),
    ('herb_garden_legumes_02', 'Hunger', 0.55),
    ('herb_garden_herbs_01',   'Hunger', 0.15),
 
    -- Market square
    ('market_bread_01',  'Hunger', 0.50),
    ('market_bread_02',  'Hunger', 0.50),
    ('market_cheese_01', 'Hunger', 0.40),
    ('market_dried_01',  'Hunger', 0.45),
    ('market_apple_01',  'Hunger', 0.25),
    ('market_apple_02',  'Hunger', 0.25),
 
    -- Forest forage — low satiety per item
    ('forest_mushrooms_01', 'Hunger', 0.30),
    ('forest_mushrooms_02', 'Hunger', 0.30),
    ('forest_mushrooms_03', 'Hunger', 0.30),
    ('forest_berries_01',   'Hunger', 0.20),
    ('forest_berries_02',   'Hunger', 0.20),
    ('forest_nuts_01',      'Hunger', 0.35),
 
    -- Forest clearing
    ('clearing_mushrooms_01', 'Hunger', 0.25),
    ('clearing_berries_01',   'Hunger', 0.18),
    ('clearing_nuts_01',      'Hunger', 0.30),
 
    -- River fish — high satiety
    ('river_fish_01',  'Hunger', 0.70),
    ('river_fish_02',  'Hunger', 0.70),
    ('river_fish_03',  'Hunger', 0.70),
    ('river_cress_01', 'Hunger', 0.15),
 
    -- Mountain rations — moderate (dried, dense)
    ('mountain_jerky_01',   'Hunger', 0.60),
    ('mountain_jerky_02',   'Hunger', 0.60),
    ('mountain_berries_01', 'Hunger', 0.15),
 
    -- ── DRINK affordances ─────────────────────────────────────────────────────
    -- All drink objects satisfy Thirst primarily.
 
    ('tavern_water_01',   'Thirst', 0.90),
    ('tavern_water_02',   'Thirst', 0.90),
    ('tavern_ale_01',     'Thirst', 0.60), -- ale hydrates but less efficiently
    ('castle_wine_01',    'Thirst', 0.50), -- wine: mild hydration
    ('castle_wine_02',    'Thirst', 0.50),
    ('courtyard_water_01','Thirst', 0.95),
    ('courtyard_water_02','Thirst', 0.95),
    ('stables_water_01',  'Thirst', 0.80),
    ('herb_garden_water_01','Thirst',0.90),
    ('market_water_01',   'Thirst', 0.90),
    ('forest_stream_01',  'Thirst', 0.85),
    ('clearing_water_01', 'Thirst', 0.85),
    ('river_water_01',    'Thirst', 0.95),
    ('river_water_02',    'Thirst', 0.95),
    ('mountain_snow_01',  'Thirst', 0.80);
 
-- ══════════════════════════════════════════════════════════════════════════════
-- NUTRITIONAL PROFILES
-- Column order: ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain
--
-- Units are per-serving gains added to NutritionState:
--   CalorieGain  — kcal equivalent (0..100 scale, engine normalises)
--   ProteinGain  — protein units (0..100 scale)
--   IronGain     — iron units (0..100 scale)
--   VitaminDGain — IU equivalent (0..100 scale)
--   HydrationGain— hydration units (0..100 scale)
--
-- VITAMIN D SCIENCE NOTE:
--   Mushrooms exposed to UV light contain ergocalciferol (D2) — 50–100 IU/serving.
--   Fish (herring, salmon) contain cholecalciferol (D3) — highest food source,
--   ~300–600 IU/serving. In a world with limited sun (winter, indoor NPCs),
--   mushrooms and fish are the only reliable dietary VitaminD sources.
--   Dairy (cheese) contains ~40 IU/serving. Meat is negligible.
--
-- IRON SCIENCE NOTE:
--   Heme iron (meat, fish) is 2–3× more bioavailable than non-heme (plants).
--   Dark rye bread and legumes contain non-heme iron but also phytates that
--   reduce absorption — modeled via lower IronGain despite similar raw content.
--   Vitamin C (berries, kale) enhances non-heme iron absorption — not yet
--   modeled in the engine but a candidate for future PhysiologyEngine work.
-- ══════════════════════════════════════════════════════════════════════════════
 
INSERT OR IGNORE INTO NutritionalProfiles
    (ObjectId, CalorieGain, ProteinGain, IronGain, VitaminDGain, HydrationGain)
VALUES
 
    -- ── TAVERN ────────────────────────────────────────────────────────────────
    --            Cal   Prot  Iron  VitD  Hydrat
    ('tavern_pottage_01', 45.0, 12.0,  3.5, NULL,   30.0),  -- vegetable stew: moderate iron from peas/lentils
    ('tavern_pottage_02', 45.0, 12.0,  3.5, NULL,   30.0),
    ('tavern_pottage_03', 45.0, 12.0,  3.5, NULL,   30.0),
    ('tavern_roast_01',   70.0, 40.0,  8.0, NULL,    5.0),  -- chicken: high protein, good heme iron
    ('tavern_roast_02',   70.0, 40.0,  8.0, NULL,    5.0),
    ('tavern_cheese_01',  35.0, 15.0,  1.0, 12.0,   5.0),  -- cheese: protein, VitD (dairy)
    ('tavern_water_01',    2.0, NULL,  NULL, NULL,   90.0),
    ('tavern_water_02',    2.0, NULL,  NULL, NULL,   90.0),
    ('tavern_ale_01', 20.0, NULL, NULL, NULL, 40.0),
 
    -- ── CASTLE HALL ───────────────────────────────────────────────────────────
    --                       Cal   Prot  Iron  VitD  Hydrat
    ('castle_salted_meat_01', 65.0, 45.0, 10.0, NULL,   2.0),  -- pork: highest protein and iron
    ('castle_salted_meat_02', 65.0, 45.0, 10.0, NULL,   2.0),
    ('castle_dried_fish_01',  50.0, 35.0,  4.0, 40.0,   3.0),  -- dried herring: VitD preserved
    ('castle_dried_fish_02',  50.0, 35.0,  4.0, 40.0,   3.0),
    ('castle_fine_bread_01',  55.0,  8.0,  2.0, NULL,   5.0),  -- white bread: low iron (refined)
    ('castle_wine_01',        15.0, NULL,  NULL, NULL,  35.0),  -- wine: calories from alcohol, partial hydration
    ('castle_wine_02',        15.0, NULL,  NULL, NULL,  35.0),
 
    -- ── COURTYARD ─────────────────────────────────────────────────────────────
    --                         Cal   Prot  Iron  VitD  Hydrat
    ('courtyard_dark_bread_01', 50.0,  7.0,  4.5, NULL,   8.0),  -- rye bread: higher iron than white
    ('courtyard_dark_bread_02', 50.0,  7.0,  4.5, NULL,   8.0),
    ('courtyard_apple_01',      18.0,  0.5,  0.5, NULL,  15.0),  -- apple: low nutrition, good fibre
    ('courtyard_apple_02',      18.0,  0.5,  0.5, NULL,  15.0),
    ('courtyard_water_01',       2.0, NULL,  NULL, NULL,  92.0),
    ('courtyard_water_02',       2.0, NULL,  NULL, NULL,  92.0),
 
    -- ── STABLES ───────────────────────────────────────────────────────────────
    ('stables_bread_01',  45.0,  6.0,  3.5, NULL,   6.0),  -- coarse bread: moderate
    ('stables_water_01',   2.0, NULL,  NULL, NULL,  85.0),
 
    -- ── HERB GARDEN ───────────────────────────────────────────────────────────
    -- Best iron-per-calorie ratio. Critical for female NPCs with menstrual cycle.
    --                        Cal   Prot  Iron  VitD  Hydrat
    ('herb_garden_kale_01',    12.0,  3.5,  6.0, NULL,  20.0),  -- kale: excellent non-heme iron
    ('herb_garden_kale_02',    12.0,  3.5,  6.0, NULL,  20.0),
    ('herb_garden_legumes_01', 40.0, 10.0,  7.5, NULL,  12.0),  -- lentils: best plant iron source
    ('herb_garden_legumes_02', 40.0, 10.0,  7.5, NULL,  12.0),
    ('herb_garden_herbs_01',    5.0,  1.0,  2.0, NULL,   8.0),  -- medicinal herbs: minor nutrition
    ('herb_garden_water_01',    2.0, NULL,  NULL, NULL,  90.0),
 
    -- ── MARKET SQUARE ─────────────────────────────────────────────────────────
    --                  Cal   Prot  Iron  VitD  Hydrat
    ('market_bread_01', 48.0,  7.0,  3.5, NULL,   7.0),
    ('market_bread_02', 48.0,  7.0,  3.5, NULL,   7.0),
    ('market_cheese_01',32.0, 14.0,  1.0, 10.0,   6.0),
    ('market_dried_01', 38.0,  9.0,  5.0, NULL,   5.0),  -- dried peas: good protein and iron
    ('market_apple_01', 18.0,  0.5,  0.5, NULL,  15.0),
    ('market_apple_02', 18.0,  0.5,  0.5, NULL,  15.0),
    ('market_water_01',  2.0, NULL,  NULL, NULL,  92.0),
 
    -- ── FOREST ────────────────────────────────────────────────────────────────
    -- MUSHROOMS = PRIMARY WINTER VITAMIN D SOURCE.
    -- Ergocalciferol content depends on UV exposure during growth.
    -- Modeled as fixed VitaminDGain — future work could scale by SeasonFraction.
    --                      Cal   Prot  Iron  VitD  Hydrat
    ('forest_mushrooms_01', 10.0,  3.0,  2.5, 18.0,  10.0),  -- VitD: ergocalciferol (D2)
    ('forest_mushrooms_02', 10.0,  3.0,  2.5, 18.0,  10.0),
    ('forest_mushrooms_03', 10.0,  3.0,  2.5, 18.0,  10.0),
    ('forest_berries_01',   15.0,  0.8,  1.5, NULL,  25.0),  -- berries: VitC (not modeled yet)
    ('forest_berries_02',   15.0,  0.8,  1.5, NULL,  25.0),
    ('forest_nuts_01',      28.0,  5.0,  2.0, NULL,   3.0),  -- nuts: fat and protein
    ('forest_stream_01',     2.0, NULL,  NULL, NULL,  88.0),
 
    -- ── FOREST CLEARING ───────────────────────────────────────────────────────
    ('clearing_mushrooms_01', 10.0,  2.5,  2.0, 15.0,  10.0),
    ('clearing_berries_01',   12.0,  0.6,  1.2, NULL,  22.0),
    ('clearing_nuts_01',      26.0,  4.5,  1.8, NULL,   3.0),
    ('clearing_water_01',      2.0, NULL,  NULL, NULL,  88.0),
 
    -- ── RIVER CROSSING ────────────────────────────────────────────────────────
    -- FISH = BEST FOOD-BASED VITAMIN D SOURCE. Also highest protein after meat.
    -- Freshwater fish (perch, pike, trout): ~150–250 IU VitD per serving.
    -- Watercress: high iron, vitamin C (enhances iron absorption — not modeled).
    --                 Cal   Prot  Iron  VitD  Hydrat
    ('river_fish_01',  55.0, 38.0,  5.0, 55.0,  10.0),  -- fresh fish: excellent all-round
    ('river_fish_02',  55.0, 38.0,  5.0, 55.0,  10.0),
    ('river_fish_03',  55.0, 38.0,  5.0, 55.0,  10.0),
    ('river_cress_01',  5.0,  2.0,  5.5, NULL,  25.0),  -- watercress: iron + hydration
    ('river_water_01',  2.0, NULL,  NULL, NULL,  95.0),
    ('river_water_02',  2.0, NULL,  NULL, NULL,  95.0),
 
    -- ── MOUNTAIN PASS ─────────────────────────────────────────────────────────
    -- Dried jerky: calorie-dense, high protein, iron — but no fresh nutrients.
    -- No VitD, no hydration. Survival food.
    --                     Cal   Prot  Iron  VitD  Hydrat
    ('mountain_jerky_01',  55.0, 42.0, 12.0, NULL,   1.0),  -- dried venison: highest iron in game
    ('mountain_jerky_02',  55.0, 42.0, 12.0, NULL,   1.0),
    ('mountain_berries_01',10.0,  0.5,  1.0, NULL,  18.0),
    ('mountain_snow_01',    1.0, NULL,  NULL, NULL,  80.0);  -- melted snow: cold but hydrating