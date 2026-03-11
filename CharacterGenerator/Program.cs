namespace CharacterGenerator
{
    using GameEngineTools;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Extensions;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using System.Threading.Tasks;
    using GFC = GameEngineTools.Constants.FileSystemConstantsForTest;

    internal class Program
    {
        private static void ClearDirectoryForRegeneration(string playerDirectory, string npcDirectory)
        {
            var playerDir = new DirectoryInfo(playerDirectory);
            var npcDir = new DirectoryInfo(npcDirectory);
            foreach (var file in playerDir.GetFiles())
            {
                file.Delete();
            }

            foreach (var file in npcDir.GetFiles())
            {
                file.Delete();
            }
        }

        private static void ExportInfo(string directory, params CharacterBase[] nppcs)
        {
            foreach (var nppc in nppcs)
            {
                var filename = string.Format("{2}_{0}_{1}.txt", nppc.Person.ToString(), nppc.Person.Id.Value, nppc.GetType().Name);
                var dirinfo = new DirectoryInfo(directory);
                dirinfo.CreateSubdirectory("Logs");
                var path = Path.Combine(directory, "Logs", filename);
                File.WriteAllText(path, nppc.PrintInfo(false));
            }
        }

        private static async Task Main(string[] args)
        {
            var pcFolderPath = GFC.pc;
            var npcFolderPath = GFC.npc;

            var spec = GameEngineToolsRuntime.LoadSpec();
            var beginSpec = spec.Calendar.DaysFromDate(1, 1, 1) * spec.TicksPerDay;
            var beginning = new WDateTime(beginSpec);

            await using var runtime = await GameEngineToolsRuntime.StartAsync(beginning, consoleLogs: false, generatedFileOptions: new GeneratedFileOptions
            {
                NPCDirectory = GFC.npc,
                PlayerDirectory = GFC.pc
            });

            var clock = (SystemClock)runtime.Clock;
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));
            var genFile = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
            var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;

            try
            {
                Console.WriteLine("Trying to import characters...");
                genFile.ImportNPPCs();
                manager.NPPCs.Clear();
                Console.WriteLine("Done");
                Console.WriteLine("Originally generated files will be deleted");
                ClearDirectoryForRegeneration(pcFolderPath, npcFolderPath);
                Console.WriteLine("Done");
            }
            catch
            {
                Console.WriteLine("File system was not originally created, so it was created right now.");
            }

            PC player = null;
            NPC significantOther = null;
            NPC friend = null;

            Console.WriteLine("Generator will generate player's character and non-playable character's...");

            player = new PC(100, manager.RandomizePerson(40, 18));
            significantOther = new NPC(100, manager.RandomizePerson(player));
            friend = new NPC(100, manager.RandomizePerson(50, 18));

            var nppcs = manager.NPPCs;
            nppcs.Add(player);
            nppcs.Add(significantOther);
            nppcs.Add(friend);

            Console.WriteLine("Done");
            Console.WriteLine("Would you like to generate other NPCs? [Y\\N]");
            var key = Console.ReadKey().Key;
            if (key != ConsoleKey.N)
            {
                Console.Write("Please type amount of newly generated NPCs:");
                if (int.TryParse(Console.ReadLine(), out var amount))
                {
                    var successfull = 0;
                    for (int i = 0; i < amount; i++)
                    {
                        try
                        {
                            var npc = new NPC(100, manager.RandomizePerson(40, 18));
                            nppcs.Add(npc);
                            successfull++;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                            Console.WriteLine("\nAt {0}/{1}", i, amount);
                            successfull--;
                        }
                    }

                    Console.WriteLine("Generation of another {0} NPCs is done.", successfull);
                }
            }

            var currentDirectory = Directory.GetCurrentDirectory();
            Console.WriteLine("Characters will be exported to {0}", currentDirectory);
            genFile.ExportNPPCs();

            ExportInfo(currentDirectory, player, significantOther, friend);

            Console.WriteLine("Done. You may close the window right now.");
            Console.ReadKey();
        }
    }
}
