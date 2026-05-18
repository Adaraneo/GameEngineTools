// CsvWorldObjectProviderTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Unit tests for <see cref="CsvWorldObjectProvider.AddObject"/>,
    /// <see cref="CsvWorldObjectProvider.FindObject"/>, and
    /// <see cref="CsvWorldObjectProvider.GetHeldBy"/>.
    /// </summary>
    /// <remarks>
    /// Each test constructs the provider with a temporary empty directory so the cache
    /// starts empty and no CSV files are loaded. This keeps tests self-contained and fast.
    /// </remarks>
    [TestClass]
    public class CsvWorldObjectProviderTests : TestBase
    {
        #region Helpers

        /// <summary>
        /// Creates a provider backed by a temporary empty directory.
        /// No CSV files exist there, so the cache starts completely empty.
        /// </summary>
        private static CsvWorldObjectProvider EmptyProvider()
            => new CsvWorldObjectProvider(Path.GetTempPath());

        private static WorldObject MakeObject(
            string id,
            string locationId,
            HumanId? heldBy = null)
            => new WorldObject
            {
                Id          = id,
                DisplayName = id,
                Category    = WorldObjectCategory.Furniture,
                LocationId  = locationId,
                Affordances = ImmutableArray<WorldObjectAffordance>.Empty,
                HeldBy      = heldBy
            };

        #endregion

        #region AddObject → GetObjectsAt

        [TestMethod]
        public void AddObject_NewLocation_ObjectReturnedByGetObjectsAt()
        {
            var provider = EmptyProvider();
            var obj = MakeObject("apple_01", "test_location");

            provider.AddObject(obj);
            var result = provider.GetObjectsAt("test_location").ToList();

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("apple_01", result[0].Id);
        }

        [TestMethod]
        public void AddObject_MultipleObjects_AllReturnedByGetObjectsAt()
        {
            var provider = EmptyProvider();
            provider.AddObject(MakeObject("key_01", "hall"));
            provider.AddObject(MakeObject("sword_01", "hall"));

            var result = provider.GetObjectsAt("hall").ToList();

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public void AddObject_DifferentLocations_ObjectsIsolatedByLocation()
        {
            var provider = EmptyProvider();
            provider.AddObject(MakeObject("candle_01", "chapel"));
            provider.AddObject(MakeObject("tome_01", "library"));

            var chapel  = provider.GetObjectsAt("chapel").ToList();
            var library = provider.GetObjectsAt("library").ToList();

            Assert.AreEqual(1, chapel.Count);
            Assert.AreEqual("candle_01", chapel[0].Id);

            Assert.AreEqual(1, library.Count);
            Assert.AreEqual("tome_01", library[0].Id);
        }

        [TestMethod]
        public void AddObject_HeldObject_NotReturnedByGetObjectsAt()
        {
            // GetObjectsAt filters out held objects — same contract as CSV-loaded objects.
            var provider = EmptyProvider();
            var holder   = new HumanId(Guid.NewGuid());
            var obj      = MakeObject("key_02", "storeroom", heldBy: holder);

            provider.AddObject(obj);
            var result = provider.GetObjectsAt("storeroom").ToList();

            Assert.AreEqual(0, result.Count, "Held objects must be filtered by GetObjectsAt.");
        }

        #endregion

        #region FindObject

        [TestMethod]
        public void FindObject_ExistingObject_ReturnsIt()
        {
            var provider = EmptyProvider();
            provider.AddObject(MakeObject("herb_01", "herb_garden"));

            var found = provider.FindObject("herb_01");

            Assert.IsNotNull(found);
            Assert.AreEqual("herb_garden", found!.LocationId);
        }

        [TestMethod]
        public void FindObject_UnknownId_ReturnsNull()
        {
            var provider = EmptyProvider();
            Assert.IsNull(provider.FindObject("does_not_exist"));
        }

        [TestMethod]
        public void FindObject_ObjectAtDifferentLocation_StillFound()
        {
            var provider = EmptyProvider();
            provider.AddObject(MakeObject("ring_01", "vault"));
            provider.AddObject(MakeObject("coin_01", "market"));

            var found = provider.FindObject("coin_01");

            Assert.IsNotNull(found);
            Assert.AreEqual("market", found!.LocationId);
        }

        #endregion

        #region GetHeldBy

        [TestMethod]
        public void GetHeldBy_HolderCarriesObject_ObjectReturned()
        {
            var provider = EmptyProvider();
            var holderId = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("lantern_01", "corridor", heldBy: holderId));

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(1, held.Count);
            Assert.AreEqual("lantern_01", held[0].Id);
        }

        [TestMethod]
        public void GetHeldBy_NoObjectsHeld_ReturnsEmpty()
        {
            var provider  = EmptyProvider();
            var holderId  = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("chest_01", "dungeon")); // not held

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(0, held.Count);
        }

        [TestMethod]
        public void GetHeldBy_ObjectsAcrossLocations_AllHeldObjectsReturned()
        {
            var provider = EmptyProvider();
            var holderId = new HumanId(Guid.NewGuid());
            provider.AddObject(MakeObject("map_01",    "tavern",   heldBy: holderId));
            provider.AddObject(MakeObject("dagger_01", "alley",    heldBy: holderId));
            provider.AddObject(MakeObject("bread_01",  "bakery")); // unrelated, not held

            var held = provider.GetHeldBy(holderId).ToList();

            Assert.AreEqual(2, held.Count);
        }

        #endregion

        #region AddObject then RemoveObject (physical travel integration)

        [TestMethod]
        public void PhysicalTravel_RemoveFromOriginal_AddAtDrop_ObjectAppearsAtDropLocation()
        {
            var provider     = EmptyProvider();
            var pickupLoc    = "warehouse";
            var dropLoc      = "market_square";
            var holderId     = new HumanId(Guid.NewGuid());

            // Simulate Take: object is in the warehouse, marked as held.
            var pickedUp = MakeObject("crate_01", pickupLoc, heldBy: holderId);
            provider.AddObject(pickedUp);

            // Simulate Drop at a different location (physical travel logic):
            provider.RemoveObject(pickupLoc, "crate_01");
            provider.AddObject(pickedUp with { LocationId = dropLoc, HeldBy = null });

            // Object must NOT be at original location.
            var atPickup = provider.GetObjectsAt(pickupLoc).ToList();
            Assert.AreEqual(0, atPickup.Count, "Object must have left the pickup location.");

            // Object must be at the drop location.
            var atDrop = provider.GetObjectsAt(dropLoc).ToList();
            Assert.AreEqual(1, atDrop.Count, "Object must appear at the drop location.");
            Assert.AreEqual("crate_01", atDrop[0].Id);
            Assert.IsNull(atDrop[0].HeldBy, "Dropped object must have no holder.");
        }

        #endregion
    }
}
