// RiverNetworkRasterizerTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System.Collections.Generic;
    using System.Linq;
    using GameEngineTools.World.Data;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class RiverNetworkRasterizerTests
    {
        private static TerrainHeightmap MakeGrid(int width, int height, double originX = 0.0, double originY = 0.0, double cellSize = 5.0) =>
            new("test", originX, originY, cellSize, width, height, new float[width * height]);

        [TestMethod]
        public void Rasterize_StraightReach_StampsALineOfCorrectOrderAndMagnitude()
        {
            var grid = MakeGrid(10, 10);
            var reach = new RiverReach("r1", "net1", "n1", "n2",
                [(0.0, 25.0), (45.0, 25.0)], StrahlerOrder: 2, ShreveMagnitude: 7, WidthMeters: 3.0);

            var (mask, magnitude, oxbow) = RiverNetworkRasterizer.Rasterize([reach], [], grid);

            Assert.AreEqual(2, (byte)mask[5 * 10 + 0]);
            Assert.AreEqual(7, magnitude[5 * 10 + 0]);
            Assert.IsTrue(oxbow.All(b => b == 0));
        }

        [TestMethod]
        public void Rasterize_TwoOverlappingReaches_KeepsTheLargerOrder()
        {
            var grid = MakeGrid(10, 10);
            var small = new RiverReach("r1", "net1", "n1", "n2", [(0.0, 25.0), (45.0, 25.0)], StrahlerOrder: 1, ShreveMagnitude: 1, WidthMeters: 2.0);
            var big = new RiverReach("r2", "net1", "n3", "n4", [(25.0, 0.0), (25.0, 45.0)], StrahlerOrder: 3, ShreveMagnitude: 50, WidthMeters: 8.0);

            var (mask, magnitude, _) = RiverNetworkRasterizer.Rasterize([small, big], [], grid);

            var crossing = 5 * 10 + 5;
            Assert.AreEqual(3, (byte)mask[crossing]);
            Assert.AreEqual(50, magnitude[crossing]);
        }

        [TestMethod]
        public void Rasterize_ReachEntirelyOutsideTargetGrid_LeavesMaskEmpty()
        {
            var grid = MakeGrid(10, 10, originX: 0.0, originY: 0.0, cellSize: 5.0); // covers world [0,50)x[0,50)
            var farAway = new RiverReach("r1", "net1", "n1", "n2", [(10_000.0, 10_000.0), (10_050.0, 10_000.0)],
                StrahlerOrder: 1, ShreveMagnitude: 1, WidthMeters: 2.0);

            var (mask, _, _) = RiverNetworkRasterizer.Rasterize([farAway], [], grid);

            Assert.IsTrue(mask.All(b => b == 0), "A reach whose whole bounding box misses the target grid must not stamp anything, and must not clamp onto an edge.");
        }

        [TestMethod]
        public void Rasterize_ReachEndpointFarOutside_DoesNotClampToADifferentTrajectory()
        {
            // Nearly-flat true line (unclamped) stays at row 5 through column 7; a clamped endpoint would already show row 4 there.
            var grid = MakeGrid(10, 10);
            var reach = new RiverReach("r1", "net1", "n1", "n2", [(25.0, 25.0), (100_025.0, 15.0)],
                StrahlerOrder: 1, ShreveMagnitude: 1, WidthMeters: 2.0);

            var (mask, _, _) = RiverNetworkRasterizer.Rasterize([reach], [], grid);

            Assert.AreEqual(1, (byte)mask[5 * 10 + 7],
                "The true (unclamped) nearly-flat trajectory should still be at row 5 by column 7 — a clamped endpoint would have already dropped to row 4.");
        }

        [TestMethod]
        public void Rasterize_OxbowLoop_StampsOxbowMaskNotRiverMask()
        {
            var grid = MakeGrid(10, 10);
            var oxbow = new OxbowLoop("o1", "net1", [(10.0, 10.0), (15.0, 10.0), (15.0, 15.0)]);

            var (mask, _, oxbowMask) = RiverNetworkRasterizer.Rasterize([], [oxbow], grid);

            Assert.IsTrue(mask.All(b => b == 0));
            Assert.AreEqual(1, oxbowMask[2 * 10 + 2]);
        }

        [TestMethod]
        public void Rasterize_DifferentTargetCellSize_ProducesADifferentlyShapedButStillPresentRiver()
        {
            // Same graph, rasterized onto two grids of different resolution over the same world area
            // — proves resolution-independence (Stage 3's actual point): both must show SOME river,
            // even though the exact cell pattern differs.
            var coarse = MakeGrid(10, 10, cellSize: 5.0); // [0,50)x[0,50)
            var fine = MakeGrid(50, 50, cellSize: 1.0);   // same area, 5x finer
            var reach = new RiverReach("r1", "net1", "n1", "n2", [(0.0, 25.0), (49.0, 25.0)], StrahlerOrder: 1, ShreveMagnitude: 1, WidthMeters: 2.0);

            var (coarseMask, _, _) = RiverNetworkRasterizer.Rasterize([reach], [], coarse);
            var (fineMask, _, _) = RiverNetworkRasterizer.Rasterize([reach], [], fine);

            Assert.IsTrue(coarseMask.Any(b => b != 0));
            Assert.IsTrue(fineMask.Any(b => b != 0));
        }
    }
}
