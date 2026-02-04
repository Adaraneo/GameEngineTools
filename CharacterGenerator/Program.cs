namespace CharacterGenerator
{
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Hosting;
    using GameEngineTools;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Extensions;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Core.Time;
    using GFC = GameEngineTools.Constants.FileSystemConstantsForTest;
    using Microsoft.Extensions.Configuration;

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
                var filename = string.Format("{2}_{0}_{1}.txt", nppc.Person.ToString(), nppc.Person.Id, nppc.GetType().Name);
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

            var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(s =>
            {
                var configProvider = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .AddJsonFile("appsettings.Relationships.json")
                .Build();

                s.AddSingleton<IClock, SystemClock>();
                s.AddSingleton<IGeneratedFile, GeneratedFile>();
                s.Configure<GeneratedFileOptions>(opt =>
                {
                    opt.NPCDirectory = npcFolderPath;
                    opt.PlayerDirectory = pcFolderPath;
                });

                s.AddSingleton<IGameEngineToolsManager, GameEngineToolsManager>();
                s.Configure<GameEngineToolsManagerOptions>(opt =>
                {
                    opt.UseConsoleLogging = true;
                });
                s.AddHostedService<GameEngineToolsManagerInitializer>();
                s.AddHostedService<SubscribersActivator>();
            })
            .Build();

            await host.RunAsync();

            var genFile = host.Services.GetRequiredService<IGeneratedFile>();
            var manager = (GameEngineToolsManager)host.Services.GetRequiredService<IGameEngineToolsManager>();

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

            player = new PC(100, manager.RandomizePerson(18, 40));
            significantOther = new NPC(100, manager.RandomizePerson(player));
            friend = new NPC(100, manager.RandomizePerson(18, 50));

            var nppcs = manager.NPPCs;
            nppcs.Add(player);
            nppcs.Add(significantOther);
            nppcs.Add(friend);

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
                            var npc = new NPC(100, manager.RandomizePerson(18, 40));
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

            Console.WriteLine("Done");
            Console.ReadKey();
        }
    }
}