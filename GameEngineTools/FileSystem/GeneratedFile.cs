// GeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Language;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Persistence;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Implementation of <see cref="IGeneratedFile"/> — serialization and deserialization of characters to/from JSON files.
    /// </summary>
    public sealed class GeneratedFile : IGeneratedFile
    {
        #region Soukromá pole

        private readonly IClock _clock;
        private readonly IGameEngineToolsManager _characterManager;
        private readonly IHumanFactory _humanFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        /// <summary>Vocabulary store, or null when the acquisition layer is not wired.</summary>
        private readonly ILexicalAcquisitionStore? _lexicon;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Initializes the instance with all its dependencies.
        /// </summary>
        /// <param name="clock">Game clock (for future timestamps in exports).</param>
        /// <param name="characterManager">Character manager (for bulk export/import).</param>
        /// <param name="humanFactory">Factory for reconstructing characters on import.</param>
        /// <param name="options">Optional configuration of directories for exported files.</param>
        /// <param name="lexicon">
        /// Per-character vocabulary store. Optional: without it exports simply carry no vocabulary and
        /// imports restore none, which is the behaviour from before the acquisition layer existed.
        /// </param>
        public GeneratedFile(
            IClock clock,
            IGameEngineToolsManager characterManager,
            IHumanFactory humanFactory,
            IOptions<GeneratedFileOptions>? options = null,
            ILexicalAcquisitionStore? lexicon = null)
        {
            _clock = clock;
            _characterManager = characterManager;
            _humanFactory = humanFactory;
            _lexicon = lexicon;

            if (options is not null)
            {
                NPCDirectory = options.Value.NPCDirectory;
                PlayerDirectory = options.Value.PlayerDirectory;
            }

            // _jsonOptions must be set in the constructor — the converters for WDateTime and WTimeSpan
            // require a ctx that is not available in a field initializer
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                Converters =
                {
                    new HumanIdJsonConverter(),
                    new WDateTimeJsonConverter(),
                    new WTimeSpanJsonConverter(),
                }
            };
        }

        #endregion Konstrukce

        #region Vlastnosti

        /// <summary>Directory for exported NPC files.</summary>
        public string NPCDirectory { get; set; }

        /// <summary>Directory for exported player files.</summary>
        public string PlayerDirectory { get; set; }

        #endregion Vlastnosti

        #region Export

        /// <summary>
        /// Exports a player character to a JSON file.
        /// </summary>
        /// <param name="player">The player character to export.</param>
        /// <returns>The name of the created file.</returns>
        public string Export(PC player)
        {
            var filename = $"{player.Person.Id.Value}.json";
            var data = BuildCharacterData(player);
            WriteJson(Path.Combine(PlayerDirectory, filename), data);
            return filename;
        }

        /// <summary>
        /// Exportuje NPC postavu do JSON souboru.
        /// </summary>
        /// <param name="npc">NPC postava k exportu.</param>
        /// <returns>The name of the created file.</returns>
        public string Export(NPC npc)
        {
            var filename = $"{npc.Person.Id.Value}.json";
            var data = BuildCharacterData(npc);
            WriteJson(Path.Combine(NPCDirectory, filename), data);
            return filename;
        }

        /// <summary>
        /// Exports all characters from the manager (the first is the player, the rest are NPCs).
        /// </summary>
        /// <param name="pathToRootDirectory">Optional root directory.</param>
        public void ExportNPPCs(string? pathToRootDirectory = null)
        {
            _ = new GenerateFileSystem(pathToRootDirectory);
            var nppcs = _characterManager.Characters.GetEnumerator();
            nppcs.MoveNext();
            Export((PC)nppcs.Current);
            while (nppcs.MoveNext())
                Export((NPC)nppcs.Current);
        }

        #endregion Export

        #region Import

        /// <summary>
        /// Importuje NPC postavu ze JSON souboru.
        /// </summary>
        /// <param name="filename">File name in <see cref="NPCDirectory"/>.</param>
        /// <returns>The reconstructed NPC character.</returns>
        public NPC ImportNPC(string filename)
        {
            var data = ReadJson(ResolveFileUnderRoot(NPCDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.GeneticBlueprint, data.AttractionProfile, Occupation: data.Occupation);
            var person = _humanFactory.Load(blueprint, data.Snapshot);
            _lexicon?.Restore(data.Id, data.Vocabulary);

            return new NPC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor = data.Armor,
                Weapon = data.Weapon,
                Person = person,
                Health = data.Health
            };
        }

        /// <summary>
        /// Imports a player character from a JSON file.
        /// </summary>
        /// <param name="filename">File name in <see cref="PlayerDirectory"/>.</param>
        /// <returns>The reconstructed player character.</returns>
        public PC ImportPC(string filename)
        {
            var data = ReadJson(ResolveFileUnderRoot(PlayerDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.GeneticBlueprint, data.AttractionProfile, Occupation: data.Occupation);
            var person = _humanFactory.Load(blueprint, data.Snapshot);
            _lexicon?.Restore(data.Id, data.Vocabulary);

            return new PC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor = data.Armor,
                Weapon = data.Weapon,
                Person = person,
                Health = data.Health
            };
        }

        /// <summary>
        /// Imports all characters from files (the first is the player, the rest are NPCs).
        /// </summary>
        /// <param name="pathToRootDirectory">Optional root directory.</param>
        public void ImportNPPCs(string? pathToRootDirectory = null)
        {
            _characterManager.Characters.Clear();
            var generateFileSystem = new GenerateFileSystem(pathToRootDirectory);
            var fileName = generateFileSystem.Filenames.GetEnumerator();
            fileName.MoveNext();
            _characterManager.Characters.Add(ImportPC(fileName.Current));
            while (fileName.MoveNext())
                _characterManager.Characters.Add(ImportNPC(fileName.Current));
        }

        #endregion Import

        #region Privátní pomocné metody

        /// <summary>Builds a <see cref="CharacterData"/> from any game character.</summary>
        private CharacterData BuildCharacterData(CharacterBase character) => new()
        {
            Vocabulary = _lexicon?.SnapshotFor(character.Person.Id),
            Id = character.Person.Id,
            Identity = character.Person.Identity,
            Biology = character.Person.Biology,
            Personality = character.Person.Personality,
            GeneticBlueprint = character.Person.GeneticBlueprint ?? throw new InvalidOperationException($"Character {character.Person.Id.Value} has no GeneticBlueprint — cannot export."),
            AttractionProfile = character.Person.AttractionProfile,
            Snapshot = character.Person.Snapshot,
            Occupation = character.Person.Snapshot.Schedule?.Occupation,
            MaxHealth = character.MaxHealth,
            Health = character.Health,
            Armor = character.Armor,
            Weapon = character.Weapon,
            Protection = character.Protection
        };

        /// <summary>
        /// Resolves a plain filename under the configured root directory and rejects path traversal.
        /// </summary>
        private static string ResolveFileUnderRoot(string root, string filename)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                throw new InvalidOperationException("Configured root directory must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new ArgumentException("Filename must not be empty.", nameof(filename));
            }

            var safeName = Path.GetFileName(filename);
            if (!string.Equals(filename, safeName, StringComparison.Ordinal))
            {
                throw new ArgumentException("Only plain file names are allowed.", nameof(filename));
            }

            var fullRoot = Path.GetFullPath(root);
            var fullPath = Path.GetFullPath(Path.Combine(fullRoot, safeName));
            var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Resolved path escaped the configured directory.");
            }

            return fullPath;
        }

        /// <summary>Writes the data as JSON to a file.</summary>
        private void WriteJson(string path, CharacterData data)
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using var file = new StreamWriter(File.Create(path));
            file.Write(json);
        }

        /// <summary>Loads and deserializes a <see cref="CharacterData"/> from a file.</summary>
        private CharacterData ReadJson(string path)
        {
            using var file = new StreamReader(File.OpenRead(path));
            return JsonSerializer.Deserialize<CharacterData>(file.ReadToEnd(), _jsonOptions)!;
        }

        #endregion Privátní pomocné metody
    }
}
