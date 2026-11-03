// WorldObjectWriteBufferTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Objects.Production;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;

    /// <summary>Merge rules, failure recovery and shutdown flushing of the write buffer, against a real SQLite database.</summary>
    [TestClass]
    public sealed class WorldObjectWriteBufferTests
    {
        private const string Loc = "wb_loc";

        private static (WorldObjectWriteBuffer Buffer, SqliteWorldObjectProvider Provider, SqliteWorldDatabase Db) Build()
        {
            var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(SqlScriptLoader.Load("schema.sql"));
            db.InsertLocation(new LocationDescriptor(Loc, "Place", 0.05, 0.01, 50, false, LocationType.Social), "r");
            var provider = new SqliteWorldObjectProvider(db);
            return (new WorldObjectWriteBuffer(provider, db), provider, db);
        }

        private static WorldObject Bread() => FoodItemCatalog.Create(PickupItemKind.Bread, Loc, new WDateTime(0), null);

        [TestMethod]
        public void ConsumeThenPickUp_LeavesTheObjectConsumed()
        {
            var (buffer, provider, _) = Build();
            var bread = Bread();
            buffer.AddObject(bread);

            buffer.ConsumeObject(Loc, bread.Id, new WDateTime(10));
            buffer.SetHeldBy(Loc, bread.Id, new HumanId(Guid.NewGuid())); // a second character grabs the same object this substep
            buffer.Flush();

            Assert.IsFalse(provider.GetObjectsAt(Loc).Any(o => o.Id == bread.Id), "A consumed object cannot be picked up afterwards.");
            Assert.IsFalse(provider.GetHeldBy(new HumanId(Guid.NewGuid())).Any());
        }

        [TestMethod]
        public void ConsumeThenReAddOfAStaleCopy_DoesNotResurrectTheObject()
        {
            var (buffer, provider, _) = Build();
            var bread = Bread();
            buffer.AddObject(bread);

            buffer.ConsumeObject(Loc, bread.Id, new WDateTime(10));
            buffer.AddObject(bread); // stale copy read before the consume
            buffer.Flush();

            Assert.IsFalse(provider.GetObjectsAt(Loc).Any(o => o.Id == bread.Id), "The pending consume must still apply after the re-add.");
        }

        [TestMethod]
        public void FailedFlush_KeepsTheMutationsForTheNextAttempt()
        {
            var (buffer, _, db) = Build();
            var bread = Bread();
            buffer.AddObject(bread);
            buffer.RemoveObject(Loc, bread.Id);
            db.Dispose(); // makes the batch fail

            try
            {
                buffer.Flush();
            }
            catch
            {
                // expected: the database is gone
            }

            Assert.AreEqual(1, buffer.PendingCount, "A failed flush must not drop its mutations.");
        }

        [TestMethod]
        public void Dispose_FlushesTheLastSubstep()
        {
            var (buffer, provider, _) = Build();
            var bread = Bread();
            buffer.AddObject(bread);

            buffer.RemoveObject(Loc, bread.Id);
            buffer.Dispose();

            Assert.IsFalse(provider.GetAllObjects().Any(o => o.Id == bread.Id), "Disposing the buffer applies what was still pending.");
        }
    }
}
