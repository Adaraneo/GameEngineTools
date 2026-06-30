// GravePersistenceTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using System;
    using System.Linq;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Objects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Verifies that a grave produced by burial survives a round trip through the real
    /// <see cref="SqliteWorldObjectProvider"/> / <see cref="SqliteWorldDatabase"/> — i.e. the new
    /// <see cref="WorldObjectCategory.Grave"/> category and the deceased id encoded in the object id
    /// persist (the <c>Category</c> column is <c>TEXT</c>, so no schema migration is needed).
    /// </summary>
    [TestClass]
    public class GravePersistenceTests
    {
        [TestMethod]
        public void Grave_PersistsThroughSqlite_AndRoundTripsDeceasedId()
        {
            var db = new SqliteWorldDatabase(":memory:");
            db.ExecuteScript(WorldDatabaseSchema.CreateTables);
            db.InsertLocation(
                new LocationDescriptor("cemetery", "Hřbitov", 0.05, 0.0, 200, false, LocationType.Public, TerrainType.Courtyard, 0, true, null),
                "Village");

            var provider = new SqliteWorldObjectProvider(db);
            var deceased = new HumanId(Guid.NewGuid());

            provider.AddObject(BurialObjects.Grave(deceased, "cemetery", "Old Tom"));

            // Read back through a fresh provider over the same db — proves it was persisted, not cached.
            var grave = new SqliteWorldObjectProvider(db)
                .GetAllObjects()
                .Single(o => o.Category == WorldObjectCategory.Grave);

            Assert.AreEqual("cemetery", grave.LocationId);
            Assert.IsTrue(BurialObjects.TryGetDeceased(grave, out var recovered) && recovered == deceased,
                "The deceased id encoded in the grave id survives the SQLite round trip.");

            // Permanent removal also persists.
            Assert.IsTrue(new SqliteWorldObjectProvider(db).RemoveObject("cemetery", grave.Id));
            Assert.IsFalse(new SqliteWorldObjectProvider(db).GetAllObjects().Any(o => o.Category == WorldObjectCategory.Grave),
                "A removed grave does not come back.");
        }
    }
}
