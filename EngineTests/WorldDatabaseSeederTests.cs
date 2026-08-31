// WorldDatabaseSeederTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="WorldDatabaseSeeder"/> — in particular that
    /// <see cref="WorldDatabaseSeeder.InitializeSchemaOnly"/> genuinely skips seed_data.sql
    /// (used by TerrainEditor's "New World" to start from a blank slate).
    /// </summary>
    [TestClass]
    public class WorldDatabaseSeederTests
    {
        [TestMethod]
        public void Initialize_FreshDatabase_SeedsNoLocations()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            WorldDatabaseSeeder.Initialize(db);

            Assert.AreEqual(0, db.GetAllLocations().Count,
                "The default seed_data.sql no longer ships any locations — worlds start empty and are authored by their own bootstrap code (e.g. GameSandbox).");
        }

        [TestMethod]
        public void InitializeSchemaOnly_FreshDatabase_LeavesLocationsEmpty()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            WorldDatabaseSeeder.InitializeSchemaOnly(db);

            Assert.AreEqual(0, db.GetAllLocations().Count,
                "InitializeSchemaOnly() must not run seed_data.sql — the world should start genuinely empty.");
        }

        [TestMethod]
        public void InitializeSchemaOnly_TablesExistAndAreQueryable()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            WorldDatabaseSeeder.InitializeSchemaOnly(db);

            // Schema (CREATE TABLE) must still have run — querying every relevant table must not throw.
            // TerrainHeightmap is no longer part of this schema at all (see InitializeTerrainDatabase).
            Assert.AreEqual(0, db.GetAllLocations().Count);
            Assert.AreEqual(0, db.GetAllConnections().Count);
        }

        [TestMethod]
        public void InitializeSchemaOnly_ThenInsertLocation_Persists()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.InitializeSchemaOnly(db);

            db.InsertLocation(
                new GameEngineTools.World.Location.LocationDescriptor(
                    Id: "hand_authored", DisplayName: "Hand Authored", BaseNoise: 0.1, NoisePerPerson: 0.02,
                    Capacity: 10, AllowsPrivacy: false, Type: GameEngineTools.World.Location.LocationType.Public),
                region: "");

            var locations = db.GetAllLocations();
            Assert.AreEqual(1, locations.Count);
            Assert.AreEqual("hand_authored", locations[0].Descriptor.Id);
        }
    }
}
