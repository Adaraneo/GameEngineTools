// GeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Persistence;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Implementace <see cref="IGeneratedFile"/> — serializace a deserializace postav do/ze JSON souborů.
    /// </summary>
    public sealed class GeneratedFile : IGeneratedFile
    {
        #region Soukromá pole

        private readonly IClock _clock;
        private readonly IGameEngineToolsManager _characterManager;
        private readonly IHumanFactory _humanFactory;
        private readonly JsonSerializerOptions _jsonOptions;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje instanci se všemi závislostmi.
        /// </summary>
        /// <param name="clock">Herní hodiny (pro budoucí timestampy v exportech).</param>
        /// <param name="ctx">
        /// Kontext světového času — potřebný pro JSON convertery <see cref="WDateTime"/>
        /// a <see cref="WTimeSpan"/>. Nahrazuje odstraněný globální <c>WDateTime.Spec</c>.
        /// </param>
        /// <param name="characterManager">Správce postav (pro hromadný export/import).</param>
        /// <param name="humanFactory">Factory pro rekonstrukci postav při importu.</param>
        /// <param name="options">Volitelná konfigurace adresářů pro export souborů.</param>
        public GeneratedFile(
            IClock clock,
            IGameEngineToolsManager characterManager,
            IHumanFactory humanFactory,
            IOptions<GeneratedFileOptions>? options = null)
        {
            _clock = clock;
            _characterManager = characterManager;
            _humanFactory = humanFactory;

            if (options is not null)
            {
                NPCDirectory = options.Value.NPCDirectory;
                PlayerDirectory = options.Value.PlayerDirectory;
            }

            // _jsonOptions musí být v konstruktoru — convertery pro WDateTime a WTimeSpan
            // vyžadují ctx, který nemáme k dispozici ve field initializeru
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

        /// <summary>Adresář pro exportované NPC soubory.</summary>
        public string NPCDirectory { get; set; }

        /// <summary>Adresář pro exportované hráčské soubory.</summary>
        public string PlayerDirectory { get; set; }

        #endregion Vlastnosti

        #region Export

        /// <summary>
        /// Exportuje hráčskou postavu do JSON souboru.
        /// </summary>
        /// <param name="player">Hráčská postava k exportu.</param>
        /// <returns>Název vytvořeného souboru.</returns>
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
        /// <returns>Název vytvořeného souboru.</returns>
        public string Export(NPC npc)
        {
            var filename = $"{npc.Person.Id.Value}.json";
            var data = BuildCharacterData(npc);
            WriteJson(Path.Combine(NPCDirectory, filename), data);
            return filename;
        }

        /// <summary>
        /// Exportuje všechny postavy ze správce (první je hráč, zbytek NPC).
        /// </summary>
        /// <param name="pathToRootDirectory">Volitelný kořenový adresář.</param>
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
        /// <param name="filename">Název souboru v <see cref="NPCDirectory"/>.</param>
        /// <returns>Rekonstruovaná NPC postava.</returns>
        public NPC ImportNPC(string filename)
        {
            var data = ReadJson(ResolveFileUnderRoot(NPCDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.GeneticBlueprint, data.AttractionProfile, Occupation: data.Occupation);
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

        /// <summary>
        /// Importuje hráčskou postavu ze JSON souboru.
        /// </summary>
        /// <param name="filename">Název souboru v <see cref="PlayerDirectory"/>.</param>
        /// <returns>Rekonstruovaná hráčská postava.</returns>
        public PC ImportPC(string filename)
        {
            var data = ReadJson(ResolveFileUnderRoot(PlayerDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.GeneticBlueprint, data.AttractionProfile, Occupation: data.Occupation);
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

        /// <summary>
        /// Importuje všechny postavy ze souborů (první je hráč, zbytek NPC).
        /// </summary>
        /// <param name="pathToRootDirectory">Volitelný kořenový adresář.</param>
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

        /// <summary>Sestaví <see cref="CharacterData"/> z libovolné herní postavy.</summary>
        private static CharacterData BuildCharacterData(CharacterBase character) => new()
        {
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

        /// <summary>Zapíše data jako JSON do souboru.</summary>
        private void WriteJson(string path, CharacterData data)
        {
            var json = JsonSerializer.Serialize(data, _jsonOptions);
            using var file = new StreamWriter(File.Create(path));
            file.Write(json);
        }

        /// <summary>Načte a deserializuje <see cref="CharacterData"/> ze souboru.</summary>
        private CharacterData ReadJson(string path)
        {
            using var file = new StreamReader(File.OpenRead(path));
            return JsonSerializer.Deserialize<CharacterData>(file.ReadToEnd(), _jsonOptions)!;
        }

        #endregion Privátní pomocné metody
    }
}
