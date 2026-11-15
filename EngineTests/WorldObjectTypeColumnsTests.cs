// WorldObjectTypeColumnsTests.cs
// Copyright (c) 50PSoftware

namespace EngineTests
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Data;
    using GameEngineTools.World.Objects;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using System;
    using System.Linq;

    /// <summary>The world database keeps only the type id, the owner and the condition of an object; the type itself lives in the catalog files.</summary>
    [TestClass]
    public sealed class WorldObjectTypeColumnsTests
    {
        private static SqliteWorldDatabase NewDb()
        {
            var db = new SqliteWorldDatabase(":memory:");
            WorldDatabaseSeeder.Initialize(db);
            db.ExecuteScript("INSERT INTO Locations (Id, DisplayName, Type) VALUES ('farm', 'Farm', 'Social');");
            return db;
        }

        [TestMethod]
        public void TypeIdOwnerAndCondition_SurviveEveryReadPath()
        {
            using var db = NewDb();
            var owner = new HumanId(Guid.NewGuid());
            db.AddObject(new WorldObject
            {
                Id = "spade_1",
                DisplayName = "Rýč",
                Category = WorldObjectCategory.Tool,
                LocationId = "farm",
                IsAvailable = true,
                IsPickable = true,
                TypeId = "spade",
                OwnerId = owner,
                Condition = 0.6,
            });
            db.AddObject(new WorldObject { Id = "plain", DisplayName = "Stůl", Category = WorldObjectCategory.Furniture, LocationId = "farm", IsAvailable = true });

            var viaLocation = db.GetObjectsAt("farm").Single(o => o.Id == "spade_1");
            var viaFind = db.FindObject("spade_1")!;
            var viaAll = db.GetAllObjectsAt("farm").Single(o => o.Id == "spade_1");
            foreach (var o in new[] { viaLocation, viaFind, viaAll })
            {
                Assert.AreEqual("spade", o.TypeId);
                Assert.AreEqual(owner, o.OwnerId);
                Assert.AreEqual(0.6, o.Condition, 1e-12);
            }

            var plain = db.GetObjectsAt("farm").Single(o => o.Id == "plain");
            Assert.IsNull(plain.TypeId);
            Assert.IsNull(plain.OwnerId);
            Assert.AreEqual(1.0, plain.Condition, "An object with no type is as good as new.");
        }

        [TestMethod]
        public void HeldAndConsumedState_StillReadsRight_NextToTheNewColumns()
        {
            using var db = NewDb();
            db.AddObject(new WorldObject { Id = "bread", DisplayName = "Chléb", Category = WorldObjectCategory.Food, LocationId = "farm", IsAvailable = true, TypeId = "bread", Condition = 0.9 });
            var holder = new HumanId(Guid.NewGuid());
            db.SetHeldBy("farm", "bread", holder);

            var held = db.GetAllObjectsAt("farm").Single(o => o.Id == "bread");

            Assert.AreEqual(holder, held.HeldBy);
            Assert.AreEqual("bread", held.TypeId);
            Assert.AreEqual(0.9, held.Condition, 1e-12);
        }

    }
}
