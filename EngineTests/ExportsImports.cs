namespace EngineTests
{
    using EngineTests.Extensions;
    using EngineTests.Utils;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;

    [TestClass]
    public class ExportsImports : TestBase
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

        [TestMethod]
        public void DoBasicRelationshipExport()
        {
            var nppcs = new List<CharacterBase>();
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson(PlayersMaxAge, null, PlayersMinAge));
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
            var player = new PC(MaxHealth, CharacterManager.RandomizePerson(15, null));
            var significantOther = new NPC(MaxHealth, CharacterManager.RandomizePerson(player));
            nppcs.Add(player);
            nppcs.Add(significantOther);
            Filenames.Add(GeneratedFile.Export(player));
            Filenames.Add(GeneratedFile.Export(significantOther));

            var clock = (TestClock)ServiceProvider.GetRequiredService<IClock>();

            GeneratedFile.Export(player);
            GeneratedFile.Export(significantOther);

            clock.Advance(WTimeSpan.FromDays(235));

            GeneratedFile.Export(player);
            GeneratedFile.Export(significantOther);

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }

        [TestMethod]
        public async Task DoSmallFamilyExport()
        {
            var nppcs = new List<CharacterBase>();
            var instance = CharacterManager;
            var familyGenerator = ServiceProvider.GetRequiredService<NuclearFamilyGenerator>();
            var familyGraph = ServiceProvider.GetRequiredService<FamilyGraph>();
            var now = ServiceProvider.GetRequiredService<IClock>().Now;

            var player = new PC(MaxHealth, instance.RandomizePerson(PlayersMinAge, null));

            var familySpec = new NuclearFamilySpec(
                new HumanBlueprintRequest(SexBiology.Male, now.Date.AddYears(-39), now.Date.AddYears(-29)), new HumanBlueprintRequest(SexBiology.Female, now.Date.AddYears(-39), now.Date.AddYears(-29)),
                [
                    new ChildSpec(now.Date.AddYears(-15)),
                    new ChildSpec(now.Date.AddYears(-12))
                ]);

            nppcs.Add(player);

            var family = familyGenerator.Generate(familySpec, familyGraph, now);

            var father = new NPC(MaxHealth, family.PartnerA);
            var mother = new NPC(MaxHealth, family.PartnerB);
            var child1 = new NPC(MaxHealth, family.Children[0]);
            var child2 = new NPC(MaxHealth, family.Children[1]);

            nppcs.Add(father);
            nppcs.Add(mother);
            nppcs.Add(child1);
            nppcs.Add(child2);

            Filenames.Add(GeneratedFile.Export(player));
            Filenames.Add(GeneratedFile.Export(father));
            Filenames.Add(GeneratedFile.Export(mother));
            Filenames.Add(GeneratedFile.Export(child1));
            Filenames.Add(GeneratedFile.Export(child2));

            this.AssertImports(this.DoImport(false), nppcs);
            this.AssertImports(this.DoImport(), nppcs);
        }

        public TestContext TestContext { get; set; }
    }
}
