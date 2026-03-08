// GeneratedFile.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.FileSystem
{
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using Microsoft.Extensions.Options;
    using GameEngineTools;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Persistence;
    using GameEngineTools.Extensions;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using GD = GameEngineTools.Constants.FileSystemConstant.GeneratedDirectory;

    /// <summary>
    /// Implementace <see cref="IGeneratedFile"/> — serializace a deserializace postav do/ze JSON souborů.
    /// </summary>
    public sealed class GeneratedFile : IGeneratedFile
    {
        #region Soukromá pole

        private readonly IClock                    _clock;
        private readonly IGameEngineToolsManager   _characterManager;
        private readonly IHumanFactory             _humanFactory;
        private readonly JsonSerializerOptions     _jsonOptions;

        #endregion

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
            IClock                              clock,
            WorldTimeContext                    ctx,
            IGameEngineToolsManager             characterManager,
            IHumanFactory                       humanFactory,
            IOptions<GeneratedFileOptions>?     options = null)
        {
            _clock            = clock;
            _characterManager = characterManager;
            _humanFactory     = humanFactory;

            if (options is not null)
            {
                NPCDirectory    = options.Value.NPCDirectory;
                PlayerDirectory = options.Value.PlayerDirectory;
            }

            // _jsonOptions musí být v konstruktoru — convertery pro WDateTime a WTimeSpan
            // vyžadují ctx, který nemáme k dispozici ve field initializeru
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented    = true,
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                Converters =
                {
                    new HumanIdJsonConverter(),
                    new WDateTimeJsonConverter(ctx),
                    new WTimeSpanJsonConverter(ctx),
                }
            };
        }

        #endregion

        #region Vlastnosti

        /// <summary>Adresář pro exportované NPC soubory.</summary>
        public string NPCDirectory { get; set; }

        /// <summary>Adresář pro exportované hráčské soubory.</summary>
        public string PlayerDirectory { get; set; }

        #endregion

        #region Export

        /// <summary>
        /// Exportuje hráčskou postavu do JSON souboru.
        /// </summary>
        /// <param name="player">Hráčská postava k exportu.</param>
        /// <returns>Název vytvořeného souboru.</returns>
        public string Export(PC player)
        {
            var filename = $"{player.Person.Id.Value}.json";
            var data     = BuildCharacterData(player);
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
            var data     = BuildCharacterData(npc);
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
            var nppcs = _characterManager.NPPCs.GetEnumerator();
            nppcs.MoveNext();
            Export((PC)nppcs.Current);
            while (nppcs.MoveNext())
                Export((NPC)nppcs.Current);
        }

        #endregion

        #region Import

        /// <summary>
        /// Importuje NPC postavu ze JSON souboru.
        /// </summary>
        /// <param name="filename">Název souboru v <see cref="NPCDirectory"/>.</param>
        /// <returns>Rekonstruovaná NPC postava.</returns>
        public NPC ImportNPC(string filename)
        {
            var data      = ReadJson(Path.Combine(NPCDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.PhysicalAppearance);
            var person    = _humanFactory.Create(blueprint);
            person.RestoreSnapshot(data.Snapshot);

            return new NPC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor     = data.Armor,
                Weapon    = data.Weapon,
                Person    = person,
                Health    = data.Health
            };
        }

        /// <summary>
        /// Importuje hráčskou postavu ze JSON souboru.
        /// </summary>
        /// <param name="filename">Název souboru v <see cref="PlayerDirectory"/>.</param>
        /// <returns>Rekonstruovaná hráčská postava.</returns>
        public PC ImportPC(string filename)
        {
            var data      = ReadJson(Path.Combine(PlayerDirectory, filename));
            var blueprint = new HumanBlueprint(data.Id, data.Identity, data.Biology, data.Personality, data.PhysicalAppearance);
            var person    = _humanFactory.Create(blueprint);
            person.RestoreSnapshot(data.Snapshot);

            return new PC
            {
                MaxHealth = (int)data.MaxHealth,
                Armor     = data.Armor,
                Weapon    = data.Weapon,
                Person    = person,
                Health    = data.Health
            };
        }

        /// <summary>
        /// Importuje všechny postavy ze souborů (první je hráč, zbytek NPC).
        /// </summary>
        /// <param name="pathToRootDirectory">Volitelný kořenový adresář.</param>
        public void ImportNPPCs(string? pathToRootDirectory = null)
        {
            _characterManager.NPPCs.Clear();
            var generateFileSystem = new GenerateFileSystem(pathToRootDirectory);
            var fileName           = generateFileSystem.Filenames.GetEnumerator();
            fileName.MoveNext();
            _characterManager.NPPCs.Add(ImportPC(fileName.Current));
            while (fileName.MoveNext())
                _characterManager.NPPCs.Add(ImportNPC(fileName.Current));
        }

        #endregion

        #region Privátní pomocné metody

        /// <summary>Sestaví <see cref="CharacterData"/> z libovolné herní postavy.</summary>
        private static CharacterData BuildCharacterData(CharacterBase character) => new()
        {
            Id                 = character.Person.Id,
            Identity           = character.Person.Identity,
            Biology            = character.Person.Biology,
            Personality        = character.Person.Personality,
            PhysicalAppearance = character.Person.PhysicalAppearance,
            Snapshot           = character.Person.Snapshot,
            MaxHealth          = character.MaxHealth,
            Health             = character.Health,
            Armor              = character.Armor,
            Weapon             = character.Weapon,
            Protection         = character.Protection
        };

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

        #endregion
    }
}
