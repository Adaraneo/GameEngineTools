// WorldObjectWriteBufferTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Collections.Immutable;
    using System.Linq;

    [TestClass]
    public class WorldObjectWriteBufferTests
    {
        #region Helpers

        private static void SeedSchema(SqliteWorldDatabase db)
        {
            var schemaSql = SqlScriptLoader.Load("schema.sql");
            db.ExecuteScript(schemaSql);
        }

        private static void SeedLocation(SqliteWorldDatabase db, string locationId)
            => db.InsertLocation(
                new LocationDescriptor(
                    Id: locationId,
                    DisplayName: locationId,
                    Type: LocationType.Public,
                    BaseNoise: 0.1,
                    NoisePerPerson: 0.01,
                    Capacity: 20,
                    AllowsPrivacy: false),
                region: string.Empty);

        private static WorldObject MakeObject(string id, string locationId, HumanId? heldBy = null)
            => new WorldObject
            {
                Id = id,
                DisplayName = id,
                Category = WorldObjectCategory.Furniture,
                LocationId = locationId,
                Affordances = ImmutableArray<WorldObjectAffordance>.Empty,
                HeldBy = heldBy,
                IsAvailable = true,
                HeatSignature = 0,
                AmbientNoise = 0,
                BlocksLineOfSight = false,
                IsPickable = false,
                WeightGrams = 0,
                ItemKind = PickupItemKind.None,
                Respawns = false,
                RespawnMinutes = 0
            };

        private static (SqliteWorldDatabase db, SqliteWorldObjectProvider provider, WorldObjectWriteBuffer buffer)
            CreateSut()
        {
            var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            var buffer = new WorldObjectWriteBuffer(provider, db);
            return (db, provider, buffer);
        }

        #endregion Helpers

        #region EmptyBuffer_FlushIsNoOp

        [TestMethod]
        public void Buffer_EmptyBuffer_FlushIsNoOp()
        {
            var (db, _, buffer) = CreateSut();
            using (db)
            {
                // Should complete without throwing — no DB calls expected.
                buffer.Flush();
                buffer.Flush();
            }
        }

        #endregion EmptyBuffer_FlushIsNoOp

        #region ConsumedThenRestored_LastWriteWins

        [TestMethod]
        public void Buffer_ConsumedThenRestored_OnlyRestoreWritten()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "kitchen");
                provider.AddObject(MakeObject("bread_01", "kitchen"));

                // Consume then restore in the same substep — buffer keeps only Restore.
                buffer.ConsumeObject("kitchen", "bread_01", new WDateTime(1000));
                buffer.RestoreObject("kitchen", "bread_01");

                buffer.Flush();

                // After flush the object must be available (restored), not consumed.
                var result = provider.GetObjectsAt("kitchen").ToList();
                Assert.AreEqual(1, result.Count, "Object must be visible (restored) after Flush.");
            }
        }

        [TestMethod]
        public void Buffer_RestoredThenConsumed_OnlyConsumeWritten()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "kitchen");
                provider.AddObject(MakeObject("bread_01", "kitchen"));
                // Pre-consume directly so we start from consumed state.
                provider.ConsumeObject("kitchen", "bread_01", new WDateTime(500));

                // Restore then consume again in the same substep — last write wins: Consume.
                buffer.RestoreObject("kitchen", "bread_01");
                buffer.ConsumeObject("kitchen", "bread_01", new WDateTime(2000));

                buffer.Flush();

                var result = provider.GetObjectsAt("kitchen").ToList();
                Assert.AreEqual(0, result.Count, "Object must be hidden (consumed) after Flush.");
            }
        }

        #endregion ConsumedThenRestored_LastWriteWins

        #region Flush_WritesAllMutationsInOneTx

        [TestMethod]
        public void Buffer_Flush_WritesAllMutationsInOneTx()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "hall");

                provider.AddObject(MakeObject("candle_01", "hall"));
                provider.AddObject(MakeObject("sword_01", "hall"));
                provider.AddObject(MakeObject("tome_01", "hall"));

                // Three different mutation kinds in a single substep.
                buffer.ConsumeObject("hall", "candle_01", new WDateTime(100));

                var holder = new HumanId(Guid.NewGuid());
                buffer.SetHeldBy("hall", "sword_01", holder);

                buffer.RemoveObject("hall", "tome_01");

                buffer.Flush();

                // candle consumed — not in GetObjectsAt (available-only query).
                var available = provider.GetObjectsAt("hall").ToList();
                Assert.IsFalse(available.Any(o => o.Id == "candle_01"), "Candle must be consumed.");
                Assert.IsFalse(available.Any(o => o.Id == "tome_01"), "Tome must be removed.");

                // GetObjectsAt excludes held objects — use GetHeldBy to confirm sword is held.
                var heldObjects = provider.GetHeldBy(holder).ToList();
                Assert.AreEqual(1, heldObjects.Count, "Sword must appear in GetHeldBy for the assigned holder.");
                Assert.AreEqual("sword_01", heldObjects[0].Id, "Sword must be held by the assigned holder.");

                // tome was permanently deleted — not in GetAllObjectsAt either.
                var allObjects = provider.GetAllObjectsAt("hall").ToList();
                Assert.IsFalse(allObjects.Any(o => o.Id == "tome_01"), "Tome must be permanently removed.");
            }
        }

        #endregion Flush_WritesAllMutationsInOneTx

        #region Individual mutation kinds

        [TestMethod]
        public void Buffer_Consume_HidesObjectFromGetObjectsAt()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "pantry");
                provider.AddObject(MakeObject("apple_01", "pantry"));

                buffer.ConsumeObject("pantry", "apple_01", new WDateTime(500));
                buffer.Flush();

                var result = provider.GetObjectsAt("pantry").ToList();
                Assert.AreEqual(0, result.Count, "Consumed object must be hidden after Flush.");
            }
        }

        [TestMethod]
        public void Buffer_Restore_ReappearsInGetObjectsAt()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "orchard");
                provider.AddObject(MakeObject("pear_01", "orchard"));
                // Consume directly via provider first.
                provider.ConsumeObject("orchard", "pear_01", new WDateTime(100));

                buffer.RestoreObject("orchard", "pear_01");
                buffer.Flush();

                var result = provider.GetObjectsAt("orchard").ToList();
                Assert.AreEqual(1, result.Count, "Restored object must reappear after Flush.");
            }
        }

        [TestMethod]
        public void Buffer_SetHeldBy_AssignsHolder()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "forge");
                provider.AddObject(MakeObject("hammer_01", "forge"));

                var holder = new HumanId(Guid.NewGuid());
                buffer.SetHeldBy("forge", "hammer_01", holder);
                buffer.Flush();

                // GetObjectsAt excludes held objects — use GetHeldBy instead.
                var held = provider.GetHeldBy(holder).ToList();
                Assert.AreEqual(1, held.Count, "Hammer must appear in GetHeldBy after Flush.");
                Assert.AreEqual("hammer_01", held[0].Id, "HeldBy must match after Flush.");
            }
        }

        [TestMethod]
        public void Buffer_SetHeldBy_Null_ClearsHolder()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                var holder = new HumanId(Guid.NewGuid());
                SeedLocation(db, "forge");
                provider.AddObject(MakeObject("hammer_01", "forge", heldBy: holder));

                buffer.SetHeldBy("forge", "hammer_01", null);
                buffer.Flush();

                var obj = provider.GetObjectsAt("forge").First();
                Assert.IsNull(obj.HeldBy, "HeldBy must be null after clearing via Flush.");
            }
        }

        [TestMethod]
        public void Buffer_Remove_DeletesObjectPermanently()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "dungeon");
                provider.AddObject(MakeObject("key_01", "dungeon"));

                buffer.RemoveObject("dungeon", "key_01");
                buffer.Flush();

                var all = provider.GetAllObjectsAt("dungeon").ToList();
                Assert.AreEqual(0, all.Count, "Removed object must not appear in GetAllObjectsAt.");
            }
        }

        #endregion Individual mutation kinds

        #region Reads pass through immediately

        [TestMethod]
        public void Buffer_GetObjectsAt_ReadsFromProviderBeforeFlush()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "garden");
                provider.AddObject(MakeObject("rose_01", "garden"));

                // No flush yet — buffer reads must see provider state immediately.
                var result = buffer.GetObjectsAt("garden").ToList();
                Assert.AreEqual(1, result.Count);
            }
        }

        [TestMethod]
        public void Buffer_AddObject_VisibleImmediatelyWithoutFlush()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "market");
                // AddObject bypasses the buffer and writes directly.
                buffer.AddObject(MakeObject("gem_01", "market"));

                var result = provider.GetObjectsAt("market").ToList();
                Assert.AreEqual(1, result.Count, "AddObject must be visible immediately without Flush.");
            }
        }

        #endregion Reads pass through immediately

        #region Multiple flushes

        [TestMethod]
        public void Buffer_SecondFlush_AfterBufferCleared_IsNoOp()
        {
            var (db, provider, buffer) = CreateSut();
            using (db)
            {
                SeedLocation(db, "cellar");
                provider.AddObject(MakeObject("barrel_01", "cellar"));

                buffer.ConsumeObject("cellar", "barrel_01", new WDateTime(10));
                buffer.Flush();

                // Second flush on already-cleared buffer — must not throw or double-apply.
                buffer.Flush();

                var result = provider.GetObjectsAt("cellar").ToList();
                Assert.AreEqual(0, result.Count, "Object must still be consumed after second no-op Flush.");
            }
        }

        #endregion Multiple flushes
    }
}
