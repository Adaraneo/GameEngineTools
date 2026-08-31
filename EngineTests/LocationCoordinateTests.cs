// LocationCoordinateTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System.Linq;

    /// <summary>
    /// Unit tests for the <c>X</c>/<c>Y</c> spatial coordinates on <see cref="LocationDescriptor"/>
    /// and their persistence via <see cref="SqliteWorldDatabase"/>.
    /// </summary>
    [TestClass]
    public class LocationCoordinateTests
    {
        #region Helpers

        /// <summary>
        /// Applies the current schema.sql to a fresh in-memory database.
        /// </summary>
        private static void SeedSchema(SqliteWorldDatabase db)
        {
            var schemaSql = SqlScriptLoader.Load("schema.sql");
            db.ExecuteScript(schemaSql);
        }

        #endregion Helpers

        #region Defaults

        [TestMethod]
        public void LocationDescriptor_XYNotSpecified_DefaultsToOriginZero()
        {
            var descriptor = new LocationDescriptor(
                Id: "unpositioned",
                DisplayName: "Unpositioned",
                BaseNoise: 0.1,
                NoisePerPerson: 0.02,
                Capacity: 10,
                AllowsPrivacy: false,
                LocationType.Public);

            Assert.AreEqual(0.0, descriptor.X);
            Assert.AreEqual(0.0, descriptor.Y);
            Assert.AreEqual(0.0, descriptor.AltitudeMeters);
        }

        #endregion Defaults

        #region Round-trip persistence

        [TestMethod]
        public void InsertLocation_WithCoordinates_GetAllLocationsReturnsSameXY()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var descriptor = new LocationDescriptor(
                Id: "plaza",
                DisplayName: "Plaza",
                BaseNoise: 0.2,
                NoisePerPerson: 0.03,
                Capacity: 40,
                AllowsPrivacy: false,
                LocationType.Public,
                X: 123.5,
                Y: -47.25,
                AltitudeMeters: 812.0);

            db.InsertLocation(descriptor, region: "City");

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "plaza");

            Assert.AreEqual(123.5, stored.Descriptor.X);
            Assert.AreEqual(-47.25, stored.Descriptor.Y);
            Assert.AreEqual(812.0, stored.Descriptor.AltitudeMeters);
        }

        [TestMethod]
        public void UpdateLocationPosition_ExistingLocation_OverwritesXYAndAltitude()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.InsertLocation(
                new LocationDescriptor(
                    Id: "tavern",
                    DisplayName: "Tavern",
                    BaseNoise: 0.4,
                    NoisePerPerson: 0.05,
                    Capacity: 20,
                    AllowsPrivacy: false,
                    LocationType.Social,
                    X: 0.0,
                    Y: 0.0,
                    AltitudeMeters: 0.0),
                region: "Village");

            var updated = db.UpdateLocationPosition("tavern", x: 55.0, y: -12.5, altitudeMeters: 340.0);

            Assert.IsTrue(updated);
            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "tavern");
            Assert.AreEqual(55.0, stored.Descriptor.X);
            Assert.AreEqual(-12.5, stored.Descriptor.Y);
            Assert.AreEqual(340.0, stored.Descriptor.AltitudeMeters);
            // Non-spatial fields untouched by the position-only update.
            Assert.AreEqual("Tavern", stored.Descriptor.DisplayName);
        }

        [TestMethod]
        public void UpdateLocationPosition_UnknownLocation_ReturnsFalse()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var updated = db.UpdateLocationPosition("does_not_exist", 1.0, 2.0, 3.0);

            Assert.IsFalse(updated);
        }

        [TestMethod]
        public void UpdateLocationRegion_ExistingLocation_Overwrites()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            db.InsertLocation(
                new LocationDescriptor("tavern", "Tavern", 0.4, 0.05, 20, false, LocationType.Social),
                region: "Village");

            var updated = db.UpdateLocationRegion("tavern", "Mountains");

            Assert.IsTrue(updated);
            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "tavern");
            Assert.AreEqual("Mountains", stored.Region);
        }

        [TestMethod]
        public void UpdateLocationRegion_UnknownLocation_ReturnsFalse()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            Assert.IsFalse(db.UpdateLocationRegion("does_not_exist", "Coast"));
        }

        [TestMethod]
        public void UpdateConnectionDistance_ExistingConnection_Overwrites()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            db.InsertLocation(
                new LocationDescriptor("tavern", "Tavern", 0.4, 0.05, 20, false, LocationType.Social),
                region: "Village");
            db.InsertLocation(
                new LocationDescriptor("market", "Market", 0.5, 0.05, 30, false, LocationType.Public),
                region: "Village");
            db.InsertConnection("tavern", "market", 80.0);

            var updated = db.UpdateConnectionDistance("tavern", "market", 132.4);

            Assert.IsTrue(updated);
            var conn = db.GetAllConnections().Single(c => c.FromId == "tavern" && c.ToId == "market");
            Assert.AreEqual(132.4, conn.DistanceMeters);
        }

        [TestMethod]
        public void UpdateConnectionDistance_UnknownConnection_ReturnsFalse()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var updated = db.UpdateConnectionDistance("a", "b", 10.0);

            Assert.IsFalse(updated);
        }

        [TestMethod]
        public void InsertLocation_CoordinatesOmitted_GetAllLocationsReturnsZero()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.InsertLocation(
                new LocationDescriptor(
                    Id: "unpositioned",
                    DisplayName: "Unpositioned",
                    BaseNoise: 0.1,
                    NoisePerPerson: 0.02,
                    Capacity: 10,
                    AllowsPrivacy: false,
                    LocationType.Public),
                region: string.Empty);

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "unpositioned");

            Assert.AreEqual(0.0, stored.Descriptor.X);
            Assert.AreEqual(0.0, stored.Descriptor.Y);
        }

        #endregion Round-trip persistence

        #region Migration from a pre-coordinate schema

        /// <summary>
        /// Minimal Locations table shape as it existed before X/Y were introduced —
        /// used to simulate a database created by an older build of the engine.
        /// </summary>
        private const string LegacyLocationsSchema = """
            CREATE TABLE Locations (
                Id              TEXT    PRIMARY KEY,
                DisplayName     TEXT    NOT NULL,
                Type            TEXT    NOT NULL,
                Region          TEXT    NOT NULL DEFAULT '',
                BaseNoise       REAL    NOT NULL DEFAULT 0.1,
                NoisePerPerson  REAL    NOT NULL DEFAULT 0.02,
                Capacity        INTEGER NOT NULL DEFAULT 20,
                AllowsPrivacy   INTEGER NOT NULL DEFAULT 0,
                Terrain         TEXT    NOT NULL DEFAULT 'Indoor',
                DangerLevel     REAL    NOT NULL DEFAULT 0.0,
                AllowsPickup    INTEGER NOT NULL DEFAULT 1,
                NormId          TEXT
            );
            """;

        [TestMethod]
        public void MigrateLocationCoordinateColumns_PreExistingLegacyRow_GainsZeroDefaultXY()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(LegacyLocationsSchema);
            db.ExecuteScript("""
                INSERT INTO Locations (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson, Capacity, AllowsPrivacy)
                VALUES ('old_tavern', 'Old Tavern', 'Social', 'Town', 0.4, 0.05, 20, 0);
                """);

            db.MigrateLocationCoordinateColumns();

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "old_tavern");

            // Pre-existing data survives the migration untouched.
            Assert.AreEqual("Old Tavern", stored.Descriptor.DisplayName);
            Assert.AreEqual(20, stored.Descriptor.Capacity);
            // New columns backfill to the documented "unpositioned" default.
            Assert.AreEqual(0.0, stored.Descriptor.X);
            Assert.AreEqual(0.0, stored.Descriptor.Y);
            Assert.AreEqual(0.0, stored.Descriptor.AltitudeMeters);
        }

        /// <summary>
        /// Locations table shape as it existed after X/Y were added but before AltitudeMeters —
        /// verifies the migration also backfills a database that's only "half migrated".
        /// </summary>
        private const string PreAltitudeLocationsSchema = """
            CREATE TABLE Locations (
                Id              TEXT    PRIMARY KEY,
                DisplayName     TEXT    NOT NULL,
                Type            TEXT    NOT NULL,
                Region          TEXT    NOT NULL DEFAULT '',
                BaseNoise       REAL    NOT NULL DEFAULT 0.1,
                NoisePerPerson  REAL    NOT NULL DEFAULT 0.02,
                Capacity        INTEGER NOT NULL DEFAULT 20,
                AllowsPrivacy   INTEGER NOT NULL DEFAULT 0,
                Terrain         TEXT    NOT NULL DEFAULT 'Indoor',
                DangerLevel     REAL    NOT NULL DEFAULT 0.0,
                AllowsPickup    INTEGER NOT NULL DEFAULT 1,
                NormId          TEXT,
                X               REAL    NOT NULL DEFAULT 0.0,
                Y               REAL    NOT NULL DEFAULT 0.0
            );
            """;

        [TestMethod]
        public void MigrateLocationCoordinateColumns_PreAltitudeSchema_BackfillsAltitudeOnly()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(PreAltitudeLocationsSchema);
            db.ExecuteScript("""
                INSERT INTO Locations (Id, DisplayName, Type, Region, BaseNoise, NoisePerPerson, Capacity, AllowsPrivacy, X, Y)
                VALUES ('hilltop', 'Hilltop', 'Public', 'Wilds', 0.1, 0.02, 10, 0, 200.0, 400.0);
                """);

            db.MigrateLocationCoordinateColumns();

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "hilltop");
            Assert.AreEqual(200.0, stored.Descriptor.X);
            Assert.AreEqual(400.0, stored.Descriptor.Y);
            Assert.AreEqual(0.0, stored.Descriptor.AltitudeMeters);
        }

        [TestMethod]
        public void MigrateLocationCoordinateColumns_NoLocationsTableYet_DoesNotThrow()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            // Locations table doesn't exist yet — migration must be a safe no-op,
            // leaving schema creation to the normal WorldDatabaseSeeder.Initialize flow.
            db.MigrateLocationCoordinateColumns();
        }

        [TestMethod]
        public void MigrateLocationCoordinateColumns_AlreadyCurrentSchema_IsIdempotent()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.InsertLocation(
                new LocationDescriptor(
                    Id: "square",
                    DisplayName: "Square",
                    BaseNoise: 0.2,
                    NoisePerPerson: 0.02,
                    Capacity: 30,
                    AllowsPrivacy: false,
                    LocationType.Public,
                    X: 10.0,
                    Y: 20.0),
                region: string.Empty);

            // Running the migration again against an already-current schema must not
            // fail or disturb existing data.
            db.MigrateLocationCoordinateColumns();
            db.MigrateLocationCoordinateColumns();

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "square");
            Assert.AreEqual(10.0, stored.Descriptor.X);
            Assert.AreEqual(20.0, stored.Descriptor.Y);
        }

        #endregion Migration from a pre-coordinate schema
    }
}
