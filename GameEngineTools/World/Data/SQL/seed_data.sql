-- seed_data.sql
-- Default world data for GameEngineTools.
-- Executed once when the database is empty (no rows in Locations).
-- All statements use INSERT OR IGNORE — safe to re-run.
-- Copyright (c) 50PSoftware
--
-- Deliberately ships NO Locations/Connections/WorldObjects — worlds start genuinely empty and are
-- authored per-caller (see GameSandbox/CastleVillageSeed.cs for a full C# port of the content that
-- used to live here). Only content that is NOT tied to specific locations belongs in this file.
--
-- COLUMN ORDER:
--   SocialNorms: Id, DisplayName, Kind, Severity, EnforcementProbability,
--                RelationalModel, CultureId, ValidFromYear, ValidToYear

-- ── Social Norms ──────────────────────────────────────────────────────────────
-- All norm contexts used in the world. Add new rows here — no C# changes needed.

INSERT OR IGNORE INTO SocialNorms
    (Id, DisplayName, Kind, Severity, EnforcementProbability, RelationalModel)
VALUES
    ('norm_funeral',       'Funeral / Mourning',      'RitualContext', 0.85, 0.90, NULL),
    ('norm_formal_work',   'Formal Workplace',         'Authority',     0.55, 0.70, 'AuthorityRanking'),
    ('norm_casual_social', 'Casual Social Gathering',  'PublicConduct', 0.20, 0.40, NULL);
