namespace CharacterGenerator
{
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
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
                File.WriteAllText(path, character.ToPortraitPrompt(psb, ppf));
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

            Console.WriteLine("Generator will generate player's character and non-playable character's...");

            PC player = new PC(100, manager.RandomizePerson(40, null, 14));
            NPC significantOther = new NPC(100, manager.RandomizePerson(player));
            NPC friend = new NPC(100, manager.RandomizePerson(40, null, 14));
            NPC friendSignificantOther = new NPC(100, manager.RandomizePerson(friend));

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
                            var npc = new NPC(100, manager.RandomizePerson(40, null, 14));
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

                Console.WriteLine("NuclearFamily is going to be generated:");

                var nfb = runtime.Services.GetRequiredService<NuclearFamilyGenerator>();
                var now = player.Person.Identity.BirthDate;
                var familyGraph = runtime.Services.GetRequiredService<FamilyGraph>();
                var personalitySpec = new PersonalitySpec(PersonalitySpec.Default.O, PersonalitySpec.Default.C, PersonalitySpec.Default.E, PersonalitySpec.Default.A, PersonalitySpec.Default.N, PersonalitySpec.Default.Corr, PersonalitySpec.Default.AttachmentWeights, PersonalitySpec.Default.CommunicationWeights with { Direct = 0.6 }, PersonalitySpec.Default.ChronotypeWeights, PersonalitySpec.Default.SociosexualityWeights, PersonalitySpec.Default.MotivationMap);
                var personalityHints = new PersonalityHints(Chronotype: GameEngineTools.Characters.Traits.Chronotype.Owl, Sociosexuality: GameEngineTools.Characters.Traits.Sociosexuality.Restricted);
                var nf = nfb.Generate(new NuclearFamilySpec
                (
                    new HumanBlueprintRequest(SexBiology.Male, now.AddYears(-30), now.AddYears(-25), personalityHints, personalitySpec, Occupation: OccupationIds.Guard),
                    new HumanBlueprintRequest(SexBiology.Female, now.AddYears(-30), now.AddYears(-25), personalityHints, personalitySpec, Occupation: OccupationIds.Healer),
                    Children:
                    [
                        new ChildSpec(now),
                    ]
                ), familyGraph, now.ToDateTime());

                foreach (var member in nf.AllMembers)
                {
                    Console.WriteLine("Name: {0}, Stadium: {1}, Age: {2}", member.ToString(), member.Stadium, member.Age);
                    member.FlushInbox();
                    characters.Add(new NPC(100, member));
                }

                familyGraph.RegisterToClan(nf.PartnerB, nf.PartnerA);

                Console.WriteLine("NuclearFamilyGeneration: complete");
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
