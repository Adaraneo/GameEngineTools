namespace CharacterGenerator
{
    using GameEngineTools;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation.Portraits;
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

        private static void ClearLogsDirectory(string directory)
        {
            var logsDirInfo = new DirectoryInfo(Path.Combine(directory, "Logs"));
            if (!logsDirInfo.Exists) return;

            var files = logsDirInfo.GetFiles();
            foreach (var file in files)
            {
                file.Delete();
            }
        }

        private static void ClearPromtpsDirectory(string directory)
        {
            var promptsDirInfo = new DirectoryInfo(Path.Combine(directory, "Logs", "Prompts"));
            if (!promptsDirInfo.Exists) return;

            var files = promptsDirInfo.GetFiles();
            foreach (var file in files)
            {
                file.Delete();
            }
        }

        private static void ExportInfo(string directory, params (string, CharacterBase)[] characters)
        {
            var dirinfo = new DirectoryInfo(directory);
            dirinfo = dirinfo.CreateSubdirectory("Logs");

            foreach (var (varName, character) in characters)
            {
                var filename = $"{character.GetType().Name}_{varName}_{character.Person.Id.Value}.txt";
                var path = Path.Combine(dirinfo.FullName, filename);
                File.WriteAllText(path, character.PrintInfo(false, true));
            }
        }

        private static void ExportPrompts(string directory, IEnumerable<CharacterBase> characters, bool femaleOnly = false)
        {
            var psb = new PortraitSpecBuilder();
            var ppf = new PortraitPromptFormatter();
            var dirinfo = new DirectoryInfo(directory);
            dirinfo = dirinfo.CreateSubdirectory("Logs").CreateSubdirectory("Prompts");

            foreach (var character in characters)
            {
                if (femaleOnly && character.Person.Biology != GameEngineTools.Characters.Core.SexBiology.Female)
                    continue;

                var filename = $"{character.Person.Id.Value}.txt";

                var path = Path.Combine(dirinfo.FullName, filename);
                File.WriteAllText(path, character.PrintPortraitInfo(psb, ppf));
            }
        }

        private static async Task Main(string[] args)
        {
            var pcFolderPath = GFC.Pc;
            var npcFolderPath = GFC.Npc;

            await using var runtime = await GameEngineToolsRuntime.StartAsync(consoleLogs: false, writeJsonLines: false, writeTextLogs: false, generatedFileOptions: new GeneratedFileOptions
            {
                NPCDirectory = GFC.Npc,
                PlayerDirectory = GFC.Pc
            });

            var clock = (SystemClock)runtime.Clock;
            clock.SetNow(WDateTime.New(WDateOnly.New(100, 1, 1)));
            var genFile = (GeneratedFile)runtime.Services.GetRequiredService<IGeneratedFile>();
            var manager = (GameEngineToolsManager)runtime.GameEngineToolsManager;

            try
            {
                Console.WriteLine("Trying to import characters...");
                genFile.ImportNPPCs();
                manager.Characters.Clear();
                Console.WriteLine("Done");
                Console.WriteLine("Originally generated files will be deleted");
                ClearDirectoryForRegeneration(pcFolderPath, npcFolderPath);
                Console.WriteLine("Done");
            }
            catch
            {
                Console.WriteLine("File system was not originally created, so it was created right now.");
                ClearDirectoryForRegeneration(pcFolderPath, npcFolderPath);
                manager.Characters.Clear();
            }

            PC player = null;
            NPC significantOther = null;
            NPC friend = null;
            NPC friendSignificantOther = null;

            Console.WriteLine("Generator will generate player's character and non-playable character's...");

            player = new PC(100, manager.RandomizePerson(40, null, 14));
            significantOther = new NPC(100, manager.RandomizePerson(player));
            friend = new NPC(100, manager.RandomizePerson(40, null, 14));
            friendSignificantOther = new NPC(100, manager.RandomizePerson(friend));

            var characters = manager.Characters;
            characters.Add(player);
            characters.Add(significantOther);
            characters.Add(friend);
            characters.Add(friendSignificantOther);

            Console.WriteLine("Done");
            Console.WriteLine("Would you like to generate other NPCs? [Y\\n]");
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
                            var npc = new NPC(100, manager.RandomizePerson(40, null, 9));
                            characters.Add(npc);
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

            ClearLogsDirectory(currentDirectory);
            ClearPromtpsDirectory(currentDirectory);
            ExportInfo(currentDirectory, (nameof(player), player), (nameof(significantOther), significantOther), (nameof(friend), friend), (nameof(friendSignificantOther), friendSignificantOther));
            ExportPrompts(currentDirectory, characters, true);

            Console.WriteLine("Done. You may close the window right now.");
            Console.ReadKey();
        }
    }
}
