using GameEngineTools.Characters.GameObjects;

namespace GameTester
{
    using System.Runtime.CompilerServices;
    using GameTester.Extensions;
    using Microsoft.Extensions.DependencyInjection;
    using GameEngineTools;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Extensions;
    using GameEngineTools.World.Utils.Time;
    using EngineTests.Utils;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Core;

    [TestClass]
    public class ExportsImports : TestBase
    {
        //private void Asserts(List<NPPC> importedNPPCs, List<NPPC> exportedNPPCs)
        //{
        //    Assert.AreEqual(importedNPPCs.Count, exportedNPPCs.Count);
        //    foreach ((var nppc, var exnppc) in importedNPPCs.Zip(exportedNPPCs))
        //    {
        //        Assert.AreEqual(nppc.Health, exnppc.Health);
        //        Assert.AreEqual(nppc.Protection, exnppc.Protection);
        //        Assert.AreEqual(nppc.MaxHealth, exnppc.MaxHealth);
        //        Assert.AreEqual(nppc.Status, exnppc.Status);
        //        if (nppc.Armor != null && exnppc.Armor != null)
        //        {
        //            Assert.AreEqual(nppc.Armor.MaxProtection, exnppc.Armor.MaxProtection);
        //            Assert.AreEqual(nppc.Armor.Protection, exnppc.Armor.Protection);
        //            Assert.AreEqual(nppc.Armor.Parts.Count, exnppc.Armor.Parts.Count);
        //            Assert.AreEqual(nppc.Armor.Name, exnppc.Armor.Name);
        //            if (nppc.Armor.Parts.Any() && exnppc.Armor.Parts.Any())
        //            {
        //                Assert.AreEqual(nppc.Armor.Parts.Count, exnppc.Armor.Parts.Count);
        //                foreach ((var ipart, var expart) in nppc.Armor.Parts.Zip(exnppc.Armor.Parts))
        //                {
        //                    Assert.AreEqual(ipart.Name, expart.Name);
        //                    Assert.AreEqual(ipart.Protection, expart.Protection);
        //                    Assert.AreEqual(ipart.MaxProtection, expart.MaxProtection);
        //                    Assert.AreEqual(ipart.TypeOfPart, expart.TypeOfPart);
        //                }
        //            }
        //        }
        //        if (nppc.Weapon != null && exnppc.Weapon != null)
        //        {
        //            Assert.AreEqual(nppc.Weapon.Name, exnppc.Weapon.Name);
        //            Assert.AreEqual(nppc.Weapon.Type, exnppc.Weapon.Type);
        //            Assert.AreEqual(nppc.Weapon.MaxHitPoints, exnppc.Weapon.MaxHitPoints);
        //            Assert.AreEqual(nppc.Weapon.HitPoints, exnppc.Weapon.HitPoints);
        //        }
        //        if (nppc.Person != null && exnppc.Person != null)
        //        {
        //            var inperson = nppc.Person;
        //            var experson = exnppc.Person;
        //            Assert.AreEqual(inperson, experson);
        //        }
        //    }
        //}

        protected override void TestInit()
        {
            base.TestInit();
            var root = GameEngineTools.Constants.TestFSConstatns.gfiles;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(GameEngineTools.Constants.TestFSConstatns.player);
                Directory.CreateDirectory(GameEngineTools.Constants.TestFSConstatns.NPCs);
            }
            CharacterManager.Initialize();
            this.CleanFiles(root);
        }

        private void DeleteTestFiles()
        {
            foreach (var file in Filenames)
            {
                File.Delete(Path.GetFullPath(file));
            }
        }

        [TestMethod]
        public void DoArmouredExport()
        {
            var nppcs = new List<CharacterBase>();
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson());
            var weapons = (List<Weapon>)CharacterManager.Items[typeof(Weapon)];
            player.Weapon = weapons.First();
            var armorParts = (List<ArmorPart>)CharacterManager.Items[typeof(ArmorPart)];
            var basicArmorParts = from armorPart in armorParts where armorPart.Name.StartsWith("Basic") select armorPart;
            player.Armor = new ArmorSet("Basic", basicArmorParts.ToList());
            nppcs.Add(player);
            Filenames.Add(GeneratedFile.Export(player));

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }

        [TestMethod]
        public void DoBasicExport()
        {
            var nppcs = new List<CharacterBase>();
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson());
            nppcs.Add(player);
            Filenames.Add(GeneratedFile.Export(player));

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }
    }

    [TestClass]
    public class ExportsImportsWithServices : AsyncTestBase
    {
        protected override void TestInit()
        {
            base.TestInit();
            var root = GameEngineTools.Constants.TestFSConstatns.gfiles;
            if (!Directory.Exists(root))
            {
                Directory.CreateDirectory(GameEngineTools.Constants.TestFSConstatns.player);
                Directory.CreateDirectory(GameEngineTools.Constants.TestFSConstatns.NPCs);
            }
            CharacterManager.Initialize();
            this.CleanFiles(root);
        }

        [TestMethod]
        public void DoBasicRelationshipExport()
        {
            var nppcs = new List<CharacterBase>();
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson(PlayersMinAge, PlayersMaxAge));
            var npc = new NPC(MaxHealth, CharacterManager.RandomizePerson(player));
            var npc2 = new NPC(MaxHealth, CharacterManager.RandomizePerson());
            nppcs.Add(player);
            nppcs.Add(npc);
            nppcs.Add(npc2);
            Filenames.Add(GeneratedFile.Export(player));
            Filenames.Add(GeneratedFile.Export(npc));
            Filenames.Add(GeneratedFile.Export(npc2));
            Assert.IsTrue(Filenames.Any());

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }

        private NPC GenerateNPC()
        {
            return new NPC(MaxHealth, CharacterManager.RandomizePerson());
        }

        [TestMethod]
        public void DoExportWithRelationshipInProgress()
        {
            var nppcs = new List<CharacterBase>();
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson(15));
            var significantOther = new NPC(MaxHealth, CharacterManager.RandomizePerson(player));
            nppcs.Add(player);
            nppcs.Add(significantOther);
            Filenames.Add(GeneratedFile.Export(player));
            Filenames.Add(GeneratedFile.Export(significantOther));

            var clock = (TestClock)ServiceProvider.GetRequiredService<IClock>();

            GeneratedFile.Export(player);
            GeneratedFile.Export(significantOther);

            clock.Advance(WorldTimeContext.Days(352));


            GeneratedFile.Export(player);
            GeneratedFile.Export(significantOther);

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }

        //[TestMethod]
        //public async Task DoSmallFamilyExport()
        //{
        //    var nppcs = new List<NPPC>();
        //    var instance = CharacterManager;
        //    var player = new PC(MaxHealth, instance.RandomizePerson(PlayersMinAge));
        //    var father = new NPC(MaxHealth, instance.RandomizePerson(29, 39, GenusType.Male));
        //    var mother = new NPC(MaxHealth, instance.RandomizePerson(29, 39, GenusType.Female));
        //    var child1 = new NPC(MaxHealth, instance.RandomizePerson(maxAge: 15));
        //    var child2 = new NPC(MaxHealth, instance.RandomizePerson(maxAge: 12));
        //    var family = FamilyBuilder.WireUp(CharacterManager.RelationshipManager, father.Person, mother.Person, child1.Person, child2.Person);
        //    father.Person = family.Father;
        //    mother.Person = family.Mother;
        //    child1.Person = family.Children[0];
        //    child2.Person = family.Children[1];
        //    nppcs.Add(player);
        //    nppcs.Add(father);
        //    nppcs.Add(mother);
        //    nppcs.Add(child1);
        //    nppcs.Add(child2);

        //    Filenames.Add(GeneratedFile.Export(player));
        //    Filenames.Add(GeneratedFile.Export(father));
        //    Filenames.Add(GeneratedFile.Export(mother));
        //    Filenames.Add(GeneratedFile.Export(child1));
        //    Filenames.Add(GeneratedFile.Export(child2));

        //    this.AssertImports(this.DoImport(false), nppcs);
        //    this.AssertImports(this.DoImport(), nppcs);
        //}

        //[TestMethod]
        //public async Task DoSmallFamilyExportWithSignificantOther()
        //{
        //    var cl = ServiceProvider.GetRequiredService<TestClock>();
        //    var peopleResolver = (InMemoryHumanResolver)ServiceProvider.GetRequiredService<IHumanResolver>();
        //    var bus = ServiceProvider.GetRequiredService<IEventBus>();
        //    var now = cl.Now;
        //    var nppcs = new List<NPPC>();
        //    var instance = CharacterManager;
        //    var player = new PC(MaxHealth, instance.RandomizePerson(25, PlayersMaxAge, GenusType.Male));
        //    var significantOther = new NPC(MaxHealth, instance.RandomizePerson(player));
        //    var father = new NPC(MaxHealth, instance.RandomizePerson(29, 39, GenusType.Male));
        //    var mother = new NPC(MaxHealth, instance.RandomizePerson(29, 39, GenusType.Female));
        //    var child1 = new NPC(MaxHealth, instance.RandomizePerson(maxAge: 15));
        //    var child2 = new NPC(MaxHealth, instance.RandomizePerson(maxAge: 12));

        //    bus.Subscribe<ScheduledInteractionEvent>(ev =>
        //    {
        //        Console.WriteLine($"Scheduled interaction: {ev.InteractionType} between {NPPC.People.FindByDNA(ev.ActorId).ToString()} and {NPPC.People.FindByDNA(ev.TargetId).ToString()} at {ev.When}");
        //        return Task.CompletedTask;
        //    });

        //    var stageThresholds = ServiceProvider.GetRequiredService<BondRulesEngine>().GetThresholds();
        //    peopleResolver.Add(father.Person);
        //    peopleResolver.Add(mother.Person);

        //    await bus.PublishAsync<ScheduledInteractionEvent>(new ScheduledInteractionEvent(father.Person.DNA, mother.Person.DNA, new Flirt(), 1, default, now.AddSeconds(5)), TestContext.CancellationTokenSource.Token);
        //    var family = FamilyBuilder.WireUp(instance.RelationshipManager, father.Person, mother.Person, child1.Person, child2.Person, options: FamilyBuilder.FamilyOptions.Simulated with { Romance = FamilyBuilder.RomancePlan.Default with { Months = 23, InteractionsPerWeek = 6, BaseEffect = 3, IncludeSex = true }, StageThresholds = stageThresholds });

        //    await bus.PublishAsync<ScheduledInteractionEvent>(new ScheduledInteractionEvent(father.Person.DNA, mother.Person.DNA, new Flirt(), 1, InteractionContext.Neutral.WithPhysicality(13f), now.AddMinutes(1)), TestContext.CancellationTokenSource.Token);
        //    cl.Advance(WTimeSpan.FromMinutes(10));
        //    var evt = await ServiceProvider.GetRequiredService<TestEventBus>().WaitForAsync<InteractionAppliedEvent>(TimeSpan.FromSeconds(5));
        //    Assert.IsNotNull(evt, "InteractionAppliedEvent not fired");
        //    var (r1, r2) = instance.RelationshipManager.GetMutualRelationship(father.Person, mother.Person);
        //    Console.WriteLine($"Compatibility {father.Person.ToString()} -> {mother.Person.ToString()}: {r1.Compatibility}");
        //    Console.WriteLine($"Compatibility {mother.Person.ToString()} -> {father.Person.ToString()}: {r2.Compatibility}");
        //    father.Person = family.Father;
        //    mother.Person = family.Mother;
        //    child1.Person = family.Children[0];
        //    child2.Person = family.Children[1];
        //    nppcs.Add(player);
        //    nppcs.Add(significantOther);
        //    nppcs.Add(father);
        //    nppcs.Add(mother);
        //    nppcs.Add(child1);
        //    nppcs.Add(child2);

        //    Filenames.Add(GeneratedFile.Export(player));
        //    Filenames.Add(GeneratedFile.Export(significantOther));
        //    Filenames.Add(GeneratedFile.Export(father));
        //    Filenames.Add(GeneratedFile.Export(mother));
        //    Filenames.Add(GeneratedFile.Export(child1));
        //    Filenames.Add(GeneratedFile.Export(child2));

        //    this.AssertImports(this.DoImport(false), nppcs);
        //    this.AssertImports(this.DoImport(), nppcs);
        //}

        public TestContext TestContext { get; set; }
    }
}