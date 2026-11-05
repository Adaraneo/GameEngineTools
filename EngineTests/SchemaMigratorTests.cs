// SchemaMigratorTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;

    /// <summary>Schema versioning of the SQLite files through PRAGMA user_version.</summary>
    [TestClass]
    public sealed class SchemaMigratorTests
    {
        [TestMethod]
        public void FreshDatabase_IsStampedWithTheLatestSchemaVersion()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            WorldDatabaseSeeder.Initialize(db);

            Assert.AreEqual(SchemaMigrator.LatestVersion(SchemaMigrator.World), db.UserVersion);
            Assert.IsTrue(db.HasColumn("WorldObjects", "Price") && db.HasColumn("WorldObjects", "ShopId"));
        }

        [TestMethod]
        public void LegacyDatabase_WithoutVersionOrNewColumns_IsMigratedEvenWithRowsAlreadyInIt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript("""
                CREATE TABLE Locations (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL);
                CREATE TABLE WorldObjects (Id TEXT PRIMARY KEY, DisplayName TEXT NOT NULL);
                INSERT INTO Locations VALUES ('old', 'Old place');
                INSERT INTO WorldObjects VALUES ('o1', 'Old object');
                """);
            Assert.AreEqual(0, db.UserVersion, "A pre-versioning file starts at 0.");

            SchemaMigrator.Apply(db, SchemaMigrator.World);

            Assert.AreEqual(SchemaMigrator.LatestVersion(SchemaMigrator.World), db.UserVersion);
            foreach (var column in new[] { "X", "Y", "AltitudeMeters", "TemperatureCelsius", "Humidity" })
                Assert.IsTrue(db.HasColumn("Locations", column), $"Locations.{column} must be added.");
            Assert.IsTrue(db.HasColumn("WorldObjects", "Price") && db.HasColumn("WorldObjects", "ShopId"));
            Assert.IsTrue(db.HasColumn("WorldObjects", "ProducedAt"), "WorldObjects.ProducedAt must be added.");
        }

        [TestMethod]
        public void AtomicScript_WithAFailingStatement_AppliesNothing()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(WorldDatabaseSchema.CreateTables);

            Assert.ThrowsExactly<Microsoft.Data.Sqlite.SqliteException>(() => db.ExecuteScriptAtomically(
                "INSERT INTO Locations (Id, DisplayName, Type) VALUES ('half', 'Half', 'Social'); INSERT INTO NoSuchTable VALUES (1);"));

            Assert.AreEqual(0, db.GetKnownLocationIds().Count, "The first statement must be rolled back with the failing one.");
        }

        [TestMethod]
        public void Migrating_Twice_ChangesNothing()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.Initialize(db);
            var version = db.UserVersion;

            SchemaMigrator.Apply(db, SchemaMigrator.World);

            Assert.AreEqual(version, db.UserVersion);
        }

        [TestMethod]
        public void DatabaseFromANewerBuild_IsRefusedInsteadOfBeingMisread()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.UserVersion = SchemaMigrator.LatestVersion(SchemaMigrator.World) + 5;

            Assert.Throws<InvalidOperationException>(() => SchemaMigrator.Apply(db, SchemaMigrator.World));
        }
    }
}
