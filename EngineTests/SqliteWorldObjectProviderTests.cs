// SqliteWorldObjectProviderTests.cs
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

    /// <summary>
    /// Unit tests for <see cref="SqliteWorldObjectProvider"/>.
    /// Verifies the same behavioural contract that <c>CsvWorldObjectProviderTests</c> covered,
    /// now against the SQLite-backed implementation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every test creates its own in-memory <see cref="SqliteWorldDatabase"/> via the
    /// <c>":memory:"</c> connection string — fully isolated, no files on disk.
    /// </para>
    /// <para>
    /// <b>Why SeedLocation?</b><br/>
    /// <c>WorldObjects.LocationId</c> has a foreign key constraint on <c>Locations(Id)</c>.
    /// Tests must seed the location row first before inserting objects into it.
    /// This reflects real production behaviour — objects can only live in known locations.
    /// </para>
    /// </remarks>
    [TestClass]
    public class SqliteWorldObjectProviderTests
    {
        #region Helpers

        /// <summary>
        /// Seeds a minimal location row so the FK constraint on
        /// <c>WorldObjects.LocationId</c> is satisfied.
        /// Call this before <see cref="SqliteWorldObjectProvider.AddObject"/>
        /// whenever the location hasn't been registered yet.
        /// </summary>
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

        /// <summary>
        /// Builds a minimal <see cref="WorldObject"/> for test use.
        /// </summary>
        private static WorldObject MakeObject(
            string id,
            string locationId,
            HumanId? heldBy = null)
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

        /// <summary>
        /// Applies schema.sql to a fresh in-memory database.
        /// Must be called before any other operation in tests that use :memory: databases,
        /// because the SqliteWorldDatabase constructor no longer creates tables automatically —
        /// that responsibility was moved to WorldDatabaseSeeder.Initialize().
        /// </summary>
        private static void SeedSchema(SqliteWorldDatabase db)
        {
            var schemaSql = SqlScriptLoader.Load("schema.sql");
            db.ExecuteScript(schemaSql);
        }

        #endregion Helpers

        #region AddObject → GetObjectsAt

        [TestMethod]
        public void AddObject_NewLocation_ObjectReturnedByGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "test_location");

            provider.AddObject(MakeObject("apple_01", "test_location"));
            var result = provider.GetObjectsAt("test_location").ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("apple_01", result[0].Id);
        }

        [TestMethod]
        public void AddObject_MultipleObjects_AllReturnedByGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "hall");

            provider.AddObject(MakeObject("key_01", "hall"));
            provider.AddObject(MakeObject("sword_01", "hall"));

            var result = provider.GetObjectsAt("hall").ToList();

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void AddObject_DifferentLocations_ObjectsIsolatedByLocation()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "chapel");
            SeedLocation(db, "library");

            provider.AddObject(MakeObject("candle_01", "chapel"));
            provider.AddObject(MakeObject("tome_01", "library"));

            var chapel = provider.GetObjectsAt("chapel").ToList();
            var library = provider.GetObjectsAt("library").ToList();

            Assert.AreEqual(1, chapel.Count);
            Assert.AreEqual("candle_01", chapel[0].Id);
            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("tome_01", library[0].Id);
        }

        [TestMethod]
        public void AddObject_HeldObject_NotReturnedByGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "storeroom");

            var holder = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("key_02", "storeroom", heldBy: holder));

            var result = provider.GetObjectsAt("storeroom").ToList();

            Assert.AreEqual(0, result.Count, "Held objects must be filtered by GetObjectsAt.");
        }

        #endregion AddObject → GetObjectsAt

        #region FindObject

        [TestMethod]
        public void FindObject_ExistingObject_ReturnsIt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "herb_garden");

            provider.AddObject(MakeObject("herb_01", "herb_garden"));

            var found = provider.FindObject("herb_01");

            Assert.IsNotNull(found);
            Assert.AreEqual("herb_garden", found!.LocationId);
        }

        [TestMethod]
        public void FindObject_UnknownId_ReturnsNull()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);

            Assert.IsNull(provider.FindObject("does_not_exist"));
        }

        [TestMethod]
        public void FindObject_ObjectAtDifferentLocation_StillFound()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "vault");
            SeedLocation(db, "market");

            provider.AddObject(MakeObject("ring_01", "vault"));
            provider.AddObject(MakeObject("coin_01", "market"));

            var found = provider.FindObject("coin_01");

            Assert.IsNotNull(found);
            Assert.AreEqual("market", found!.LocationId);
        }

        #endregion FindObject

        #region GetHeldBy

        [TestMethod]
        public void GetHeldBy_HolderCarriesObject_ObjectReturned()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "corridor");

            var holderId = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("lantern_01", "corridor", heldBy: holderId));

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(1, held.Count);
            Assert.AreEqual("lantern_01", held[0].Id);
        }

        [TestMethod]
        public void GetHeldBy_NoObjectsHeld_ReturnsEmpty()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "dungeon");

            var holderId = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("chest_01", "dungeon")); // not held

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(0, held.Count);
        }

        [TestMethod]
        public void GetHeldBy_ObjectsAcrossLocations_AllHeldObjectsReturned()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "tavern");
            SeedLocation(db, "alley");
            SeedLocation(db, "bakery");

            var holderId = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("map_01", "tavern", heldBy: holderId));
            provider.AddObject(MakeObject("dagger_01", "alley", heldBy: holderId));
            provider.AddObject(MakeObject("bread_01", "bakery")); // not held

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(2, held.Count);
        }

        #endregion GetHeldBy

        #region ConsumeObject + RestoreObject

        [TestMethod]
        public void ConsumeObject_ExistingObject_HiddenFromGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "kitchen");

            provider.AddObject(MakeObject("bread_01", "kitchen"));

            var consumed = provider.ConsumeObject("kitchen", "bread_01", new WDateTime(1000));
            var result = provider.GetObjectsAt("kitchen").ToList();

            Assert.IsTrue(consumed, "ConsumeObject must return true for a known object.");
            Assert.AreEqual(0, result.Count, "Consumed object must not appear in GetObjectsAt.");
        }

        [TestMethod]
        public void RestoreObject_AfterConsume_ReappearsInGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "orchard");

            provider.AddObject(MakeObject("apple_01", "orchard"));
            provider.ConsumeObject("orchard", "apple_01", new WDateTime(1000));

            provider.RestoreObject("orchard", "apple_01");
            var result = provider.GetObjectsAt("orchard").ToList();

            Assert.AreEqual(1, result.Count, "Restored object must reappear in GetObjectsAt.");
        }

        [TestMethod]
        public void ConsumeObject_UnknownObject_ReturnsFalse()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);

            var result = provider.ConsumeObject("nowhere", "ghost_obj", new WDateTime(1));

            Assert.IsFalse(result, "ConsumeObject must return false for an unknown object.");
        }

        #endregion ConsumeObject + RestoreObject

        #region RemoveObject

        [TestMethod]
        public void RemoveObject_ExistingObject_DisappearsFromGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "cellar");

            provider.AddObject(MakeObject("barrel_01", "cellar"));

            var removed = provider.RemoveObject("cellar", "barrel_01");
            var result = provider.GetObjectsAt("cellar").ToList();

            Assert.IsTrue(removed);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void RemoveObject_UnknownObject_ReturnsFalse()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);

            Assert.IsFalse(provider.RemoveObject("nowhere", "ghost_obj"));
        }

        #endregion RemoveObject

        #region SetHeldBy

        [TestMethod]
        public void SetHeldBy_AssignHolder_HidesObjectFromGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "cave");

            var holder = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("torch_01", "cave"));

            provider.SetHeldBy("cave", "torch_01", holder);
            var result = provider.GetObjectsAt("cave").ToList();

            Assert.AreEqual(0, result.Count,
                "Object assigned to a holder must not appear in GetObjectsAt.");
        }

        [TestMethod]
        public void SetHeldBy_ClearHolder_ReappearsInGetObjectsAt()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "cave");

            var holder = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("torch_01", "cave", heldBy: holder));

            provider.SetHeldBy("cave", "torch_01", null);
            var result = provider.GetObjectsAt("cave").ToList();

            Assert.AreEqual(1, result.Count,
                "Object with cleared holder must reappear in GetObjectsAt.");
        }

        #endregion SetHeldBy

        #region PhysicalTravel integration

        [TestMethod]
        public void PhysicalTravel_RemoveFromOriginal_AddAtDrop_ObjectAppearsAtDropLocation()
        {
            using var db = new SqliteWorldDatabase(":memory:");
            SeedSchema(db);
            var provider = new SqliteWorldObjectProvider(db);
            SeedLocation(db, "warehouse");
            SeedLocation(db, "market_square");

            var holderId = new HumanId(Guid.NewGuid());
            var pickedUp = MakeObject("crate_01", "warehouse", heldBy: holderId);
            provider.AddObject(pickedUp);

            // Simulate Drop at different location.
            provider.RemoveObject("warehouse", "crate_01");
            provider.AddObject(pickedUp with { LocationId = "market_square", HeldBy = null });

            var atPickup = provider.GetObjectsAt("warehouse").ToList();
            Assert.AreEqual(0, atPickup.Count, "Object must have left the pickup location.");

            var atDrop = provider.GetObjectsAt("market_square").ToList();
            Assert.AreEqual(1, atDrop.Count, "Object must appear at the drop location.");
            Assert.AreEqual("crate_01", atDrop[0].Id);
            Assert.IsNull(atDrop[0].HeldBy, "Dropped object must have no holder.");
        }

        #endregion PhysicalTravel integration

        #region Isolation — each test gets a fresh database

        [TestMethod]
        public void Isolation_TwoProviders_DoNotShareData()
        {
            // Verify that two separate in-memory providers are fully isolated.
            using var dbA = new SqliteWorldDatabase(":memory:");
            using var dbB = new SqliteWorldDatabase(":memory:");
            SeedSchema(dbA);
            SeedSchema(dbB);
            var providerA = new SqliteWorldObjectProvider(dbA);
            var providerB = new SqliteWorldObjectProvider(dbB);

            SeedLocation(dbA, "room_a");

            providerA.AddObject(MakeObject("exclusive_01", "room_a"));

            var inA = providerA.GetObjectsAt("room_a").ToList();
            var inB = providerB.GetObjectsAt("room_a").ToList();

            Assert.AreEqual(1, inA.Count, "Provider A must see its own object.");
            Assert.AreEqual(0, inB.Count, "Provider B must not see Provider A's objects.");
        }

        #endregion Isolation — each test gets a fresh database
    }
}
