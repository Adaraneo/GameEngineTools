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
    using GameEngineTools.Characters.Persistence;
    using GameEngineTools.Characters.Hosting;
    using System.Net.Http.Headers;

    public sealed class GeneratedFile : IGeneratedFile
    {
        private IClock _clock;
        private IGameEngineToolsManager _characterManager;
        private readonly IHumanFactory _humanFactory;
        public GeneratedFile(IClock clock, IGameEngineToolsManager characterManager, IHumanFactory humanFactory, IOptions<GeneratedFileOptions> options = null)
        {
            _clock = clock;
            this._characterManager = characterManager;
            _humanFactory = humanFactory;
            if (options != null)
            {
                NPCDirectory = options.Value.NPCDirectory;
                PlayerDirectory = options.Value.PlayerDirectory;
            }
            //else
            //{
            //    NPCDirectory = GD.npcs;
            //    PlayerDirectory = GD.player;
            //}
        }
        public string NPCDirectory { get; set; }
        public string PlayerDirectory { get; set; }

        public string Export(PC player)
        {
            var filename = string.Format("{0}.json", player.Person.Id.Value);

            var data = new CharacterData
            {
                Id = player.Person.Id,
                Identity = player.Person.Identity,
                Biology = player.Person.Biology,
                Personality = player.Person.Personality,
                Snapshot = player.Person.Snapshot,
                MaxHealth = player.MaxHealth,
                Health = player.Health,
                Armor = player.Armor,
                Weapon = player.Weapon,
                Protection = player.Protection
            };

            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };

            var jsonObj = JsonSerializer.Serialize(data, jsonOptions);
            using var file = new StreamWriter(File.Create($"{Path.Combine(PlayerDirectory, filename)}"));
            file.Write(jsonObj);

            return filename;
        }

        public string Export(NPC npc)
        {
            var filename = string.Format("{0}.json", npc.Person.Id.Value);
            var data = new CharacterData
            {
                Id = npc.Person.Id,
                Identity = npc.Person.Identity,
                Biology = npc.Person.Biology,
                Personality = npc.Person.Personality,
                Snapshot = npc.Person.Snapshot,
                MaxHealth = npc.MaxHealth,
                Health = npc.Health,
                Armor = npc.Armor,
                Weapon = npc.Weapon,
                Protection = npc.Protection
            };

            var jsonOptions = new JsonSerializerOptions()
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
            };

            var jsonObj = JsonSerializer.Serialize(data, jsonOptions);
            using var file = new StreamWriter(File.Create($"{Path.Combine(NPCDirectory, filename)}"));
            file.Write(jsonObj);

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
            using var  file = new StreamReader(File.OpenRead($"{Path.Combine(NPCDirectory, filename)}"));
            var data = JsonSerializer.Deserialize<CharacterData>(file.ReadToEnd(), jsonOptions);

            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality);
            var person = _humanFactory.Create(blueprint);
            person.RestoreSnapshot(data.Snapshot);

            return new NPC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor = data.Armor,
                Weapon = data.Weapon,
                Person = person,
                Health = data.Health
            };
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
            using var file = new StreamReader(File.OpenRead($"{Path.Combine(PlayerDirectory, filename)}"));
            var data = JsonSerializer.Deserialize<CharacterData>(file.ReadToEnd(), jsonOptions);

            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality);
            var person = _humanFactory.Create(blueprint);
            person.RestoreSnapshot(data.Snapshot);

            return new PC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor = data.Armor,
                Weapon = data.Weapon,
                Person = person,
                Health = data.Health
            };
        }
    }
}
