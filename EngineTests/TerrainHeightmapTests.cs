// TerrainHeightmapTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="TerrainHeightmap"/> (byte packing, bilinear sampling) and its
    /// persistence via <see cref="SqliteWorldDatabase.SaveHeightmap"/>/<see cref="SqliteWorldDatabase.LoadHeightmap"/>.
    /// </summary>
    [TestClass]
    public class TerrainHeightmapTests
    {
        private static void SeedSchema(SqliteWorldDatabase db)
        {
            // TerrainHeightmap now lives in the dedicated terrain schema, not the main world
            // schema.sql — see WorldDatabaseSeeder.InitializeTerrainDatabase.
            var schemaSql = SqlScriptLoader.Load("terrain_schema.sql");
            db.ExecuteScript(schemaSql);
        }

        private static TerrainHeightmap MakeGrid()
            // 3x2 grid, cell size 10m, origin at world (0,0):
            //   row0: 0, 10, 20
            //   row1: 5, 15, 25
            => new(
                Id: "default",
                OriginX: 0.0,
                OriginY: 0.0,
                CellSizeMeters: 10.0,
                Width: 3,
                Height: 2,
                Values: [0f, 10f, 20f, 5f, 15f, 25f]);

        #region Byte packing

        [TestMethod]
        public void ToBytes_ThenValuesFromBytes_RoundTripsExactly()
        {
            var grid = MakeGrid();

            var bytes = grid.ToBytes();
            var restored = TerrainHeightmap.ValuesFromBytes(bytes, grid.Width, grid.Height);

            CollectionAssert.AreEqual(grid.Values, restored);
        }

        [TestMethod]
        public void ValuesFromBytes_WrongLength_Throws()
        {
            var tooShort = new byte[4]; // 1 float, but grid claims 3x2 = 6 floats

            Assert.Throws<System.ArgumentException>(
                () => TerrainHeightmap.ValuesFromBytes(tooShort, width: 3, height: 2));
        }

        #endregion Byte packing

        #region Bilinear sampling

        [TestMethod]
        public void SampleAt_ExactGridPoint_ReturnsStoredValue()
        {
            var grid = MakeGrid();

            Assert.AreEqual(0.0, grid.SampleAt(0, 0), 1e-9);
            Assert.AreEqual(20.0, grid.SampleAt(20, 0), 1e-9);
            Assert.AreEqual(25.0, grid.SampleAt(20, 10), 1e-9);
        }

        [TestMethod]
        public void SampleAt_Midpoint_Interpolates()
        {
            var grid = MakeGrid();

            // Midway between (0,0)=0 and (10,0)=10 along X.
            Assert.AreEqual(5.0, grid.SampleAt(5, 0), 1e-9);
            // Midway between (0,0)=0 and (0,10)=5 along Y.
            Assert.AreEqual(2.5, grid.SampleAt(0, 5), 1e-9);
        }

        [TestMethod]
        public void SampleAt_OutsideGrid_ClampsToEdge()
        {
            var grid = MakeGrid();

            Assert.AreEqual(grid.SampleAt(0, 0), grid.SampleAt(-100, -100), 1e-9);
            Assert.AreEqual(grid.SampleAt(20, 10), grid.SampleAt(1000, 1000), 1e-9);
        }

        #endregion Bilinear sampling

        #region River mask

        [TestMethod]
        public void IsRiver_NoMask_AlwaysFalse()
        {
            var grid = MakeGrid();

            Assert.IsFalse(grid.IsRiver(0, 0));
            Assert.IsFalse(grid.IsRiver(1, 1));
        }

        [TestMethod]
        public void IsRiver_WithMask_ReflectsFlags()
        {
            var grid = MakeGrid() with { RiverMask = [0, 1, 0, 0, 0, 1] }; // (1,0) and (2,1)

            Assert.IsFalse(grid.IsRiver(0, 0));
            Assert.IsTrue(grid.IsRiver(1, 0));
            Assert.IsTrue(grid.IsRiver(2, 1));
            Assert.IsFalse(grid.IsRiver(0, 1));
        }

        [TestMethod]
        public void IsRiver_OutsideGrid_ClampsToEdge()
        {
            var grid = MakeGrid() with { RiverMask = [0, 0, 1, 0, 0, 0] }; // (2,0)

            Assert.AreEqual(grid.IsRiver(2, 0), grid.IsRiver(1000, -1000));
        }

        #endregion River mask

        #region Persistence round-trip

        [TestMethod]
        public void SaveHeightmap_ThenLoadHeightmap_ReturnsEquivalentGrid()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var grid = MakeGrid();
            db.SaveHeightmap(grid);

            var loaded = db.LoadHeightmap("default");

            Assert.IsNotNull(loaded);
            Assert.AreEqual(grid.Id, loaded!.Id);
            Assert.AreEqual(grid.OriginX, loaded.OriginX);
            Assert.AreEqual(grid.OriginY, loaded.OriginY);
            Assert.AreEqual(grid.CellSizeMeters, loaded.CellSizeMeters);
            Assert.AreEqual(grid.Width, loaded.Width);
            Assert.AreEqual(grid.Height, loaded.Height);
            CollectionAssert.AreEqual(grid.Values, loaded.Values);
        }

        [TestMethod]
        public void SaveHeightmap_WithRiverMask_RoundTrips()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var grid = MakeGrid() with { RiverMask = [0, 1, 1, 0, 0, 1] };
            db.SaveHeightmap(grid);

            var loaded = db.LoadHeightmap("default");

            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(grid.RiverMask, loaded!.RiverMask);
        }

        [TestMethod]
        public void SaveHeightmap_NoRiverMask_LoadsAsNull()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.SaveHeightmap(MakeGrid());

            var loaded = db.LoadHeightmap("default");

            Assert.IsNotNull(loaded);
            Assert.IsNull(loaded!.RiverMask);
        }

        [TestMethod]
        public void LoadHeightmap_UnknownId_ReturnsNull()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            Assert.IsNull(db.LoadHeightmap("does_not_exist"));
        }

        [TestMethod]
        public void SaveHeightmap_CalledTwiceForSameId_Overwrites()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            db.SaveHeightmap(MakeGrid());

            var repainted = MakeGrid() with { Values = [1f, 2f, 3f, 4f, 5f, 6f] };
            db.SaveHeightmap(repainted);

            var loaded = db.LoadHeightmap("default");
            CollectionAssert.AreEqual(repainted.Values, loaded!.Values);
        }

        #endregion Persistence round-trip

        #region Migration from a pre-river schema

        /// <summary>TerrainHeightmap shape as it existed before RiverMask was introduced.</summary>
        private const string PreRiverHeightmapSchema = """
            CREATE TABLE TerrainHeightmap (
                Id              TEXT    PRIMARY KEY,
                OriginX         REAL    NOT NULL,
                OriginY         REAL    NOT NULL,
                CellSizeMeters  REAL    NOT NULL,
                Width           INTEGER NOT NULL,
                Height          INTEGER NOT NULL,
                Data            BLOB    NOT NULL
            );
            """;

        [TestMethod]
        public void MigrateTerrainHeightmapColumns_PreRiverRow_SurvivesAndGainsNullRiverMask()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(PreRiverHeightmapSchema);

            var grid = MakeGrid();
            db.ExecuteScript($"""
                INSERT INTO TerrainHeightmap (Id, OriginX, OriginY, CellSizeMeters, Width, Height, Data)
                VALUES ('default', 0.0, 0.0, 10.0, 3, 2, X'{Convert.ToHexString(grid.ToBytes())}');
                """);

            db.MigrateTerrainHeightmapColumns();

            var loaded = db.LoadHeightmap("default");
            Assert.IsNotNull(loaded);
            CollectionAssert.AreEqual(grid.Values, loaded!.Values);
            Assert.IsNull(loaded.RiverMask);
        }

        [TestMethod]
        public void MigrateTerrainHeightmapColumns_NoTableYet_DoesNotThrow()
        {
            using var db = new SqliteWorldDatabase(":memory:");

            db.MigrateTerrainHeightmapColumns();
        }

        [TestMethod]
        public void MigrateTerrainHeightmapColumns_AlreadyCurrentSchema_IsIdempotent()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);

            var grid = MakeGrid() with { RiverMask = [1, 0, 0, 1, 0, 0] };
            db.SaveHeightmap(grid);

            db.MigrateTerrainHeightmapColumns();
            db.MigrateTerrainHeightmapColumns();

            var loaded = db.LoadHeightmap("default");
            CollectionAssert.AreEqual(grid.RiverMask, loaded!.RiverMask);
        }

        #endregion Migration from a pre-river schema
    }
}
