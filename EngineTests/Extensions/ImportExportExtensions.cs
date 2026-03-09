using GameEngineTools.Characters.GameObjects;

namespace GameTester.Extensions
{
    public static class ImportExportExtensions
    {
        public static void CleanFiles(this TestBase _, string rootPath)
        {
            foreach (var dirs in Directory.GetDirectories(rootPath))
            {
                foreach (var file in Directory.GetFiles(dirs))
                {
                    File.Delete(Path.GetFullPath(file));
                }
                Assert.AreEqual(0, Directory.GetFiles(dirs).Length);
            }
        }

        public static void AssertImports(this TestBase _, List<CharacterBase> importedNPPCs, List<CharacterBase> exportedNPPCs)
        {
            Assert.AreEqual(importedNPPCs.Count, exportedNPPCs.Count);
            foreach ((var nppc, var exnppc) in importedNPPCs.Zip(exportedNPPCs))
            {
                Assert.AreEqual(nppc, exnppc, $"Imported:{nppc.Person.ToString()}\nExported:{exnppc.Person.ToString()}");
            }
        }

        public static List<CharacterBase> DoImport(this TestBase abstractTest, bool useGameManagerInstance = true)
        {
            if (useGameManagerInstance)
            {
                abstractTest.Import();
                return abstractTest.CharacterManager.NPPCs;
            }
            else
            {
                abstractTest.Import(out var nppcs);
                return nppcs;
            }
        }
    }
}
