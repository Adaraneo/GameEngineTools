// GeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Microsoft.Extensions.Options;
    using GameEngineTools;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Extensions;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.Characters.Core;
    using GD = GameEngineTools.Constants.FileSystemConstant.GeneratedDirectory;

    //TODO: Solve the persistence!!!

    public sealed class GeneratedFile : IGeneratedFile
    {
        private IClock _clock;
        private IGameEngineToolsManager _characterManager;
        public GeneratedFile(IClock clock, IGameEngineToolsManager characterManager, IOptions<GeneratedFileOptions> options = null)
        {
            _clock = clock;
            this._characterManager = characterManager;
            if (options != null)
            {
                NPCDirectory = options.Value.NPCDirectory;
                PlayerDirectory = options.Value.PlayerDirectory;
            }
            else
            {
                NPCDirectory = GD.npcs;
                PlayerDirectory = GD.player;
            }
        }
        public string NPCDirectory { get; set; }
        public string PlayerDirectory { get; set; }

        public string Export(PC player)
        {
            var filename = string.Format("{0}.json", player.Person.Id);
            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };

            var hc = player.Person;

            var jsonObj = JsonSerializer.Serialize<IHuman>(hc, jsonOptions);
            StreamWriter file = new StreamWriter(File.Create($"{Path.Combine(PlayerDirectory, filename)}"));
            file.Write(jsonObj);
            file.Close();
            return filename;
        }

        public string Export(NPC npc)
        {
            var filename = string.Format("{0}.json", npc.Person.Id);
            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
            };

            var jsonObj = JsonSerializer.Serialize(npc.Person, jsonOptions);
            StreamWriter file = new StreamWriter(File.Create($"{Path.Combine(NPCDirectory, filename)}"));
            file.Write(jsonObj);
            file.Close();
            return filename;
        }

        public void ExportNPPCs(string pathToRootDirectory = null)
        {
            _ = new GenerateFileSystem(pathToRootDirectory);
            var nppcs = _characterManager.NPPCs.GetEnumerator();
            nppcs.MoveNext();
            var player = (PC)nppcs.Current;
            Export(player);
            while (nppcs.MoveNext())
            {
                Export((NPC)nppcs.Current);
            }
        }

        public NPC ImportNPC(string filename)
        {
            var jsonOptions = new JsonSerializerOptions();
            StreamReader file = new StreamReader(File.OpenRead($"{Path.Combine(NPCDirectory, filename)}"));
            var npc = JsonSerializer.Deserialize<NPC>(file.ReadToEnd(), jsonOptions);
            file.Close();

            var dna = filename.Substring(0, (filename.Length - new FileInfo(filename).Extension.Length));
            var person = npc!.Person;
            npc.Person = person;

            return npc;
        }

        public void ImportNPPCs(string pathToRootDirectory = null)
        {
            _characterManager.NPPCs.Clear();
            GenerateFileSystem generateFileSystem = new GenerateFileSystem(pathToRootDirectory);
            try
            {
                var fileName = generateFileSystem.Filenames.GetEnumerator();
                fileName.MoveNext();
                _characterManager.NPPCs.Add(ImportPC(fileName.Current));
                while (fileName.MoveNext())
                {
                    _characterManager.NPPCs.Add(ImportNPC(fileName.Current));
                }
            }
            catch
            {
                throw;
            }
        }

        public PC ImportPC(string filename)
        {
            var jsonOptions = new JsonSerializerOptions();
            StreamReader file = new StreamReader(File.OpenRead($"{Path.Combine(PlayerDirectory, filename)}"));
            var pc = JsonSerializer.Deserialize<PC>(file.ReadToEnd(), jsonOptions);
            file.Close();

            var dna = filename.Substring(0, (filename.Length - new FileInfo(filename).Extension.Length));
            var person = pc!.Person;
            pc.Person = person;

            return pc;
        }
    }
}
