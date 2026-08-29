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
                Y: -47.25);

            db.InsertLocation(descriptor, region: "City");

            var stored = db.GetAllLocations().Single(l => l.Descriptor.Id == "plaza");

            Assert.AreEqual(123.5, stored.Descriptor.X);
            Assert.AreEqual(-47.25, stored.Descriptor.Y);
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
