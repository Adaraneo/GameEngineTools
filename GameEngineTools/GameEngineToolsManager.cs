// GameEngineToolsManager.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System.Globalization;
    using GameEngineTools.Armory;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Constants;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Main game-world manager — initializes resources, manages characters
    /// and provides factory methods for generating new characters.
    /// </summary>
    public sealed class GameEngineToolsManager : IGameEngineToolsManager
    {
        #region Soukromá pole

        private readonly ILogger<GameEngineToolsManager> _log;
        private readonly GameEngineToolsManagerOptions _opt;
        private readonly IClock _clock;
        private readonly IRandomSourceFactory _rngFactory;
        private readonly IServiceProvider _serviceProvider;

        private List<ArmorPart> _armorParts = new();
        private List<Weapon> _weapons = new();

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Initializes the manager with all its dependencies via DI.
        /// </summary>
        /// <param name="clock">Source of the current game time (for RandomizePerson).</param>
        /// <param name="rngFactory">Factory for random-number generators.</param>
        /// <param name="opt">Manager configuration options (logs, directories).</param>
        /// <param name="log">Logger.</param>
        /// <param name="serviceProvider">DI provider pro lazy-resolve factories.</param>
        public GameEngineToolsManager(
            IClock clock,
            IRandomSourceFactory rngFactory,
            IOptions<GameEngineToolsManagerOptions> opt,
            ILogger<GameEngineToolsManager> log,
            IServiceProvider serviceProvider)
        {
            _clock = clock;
            _rngFactory = rngFactory;
            _opt = opt.Value;
            _log = log;
            _serviceProvider = serviceProvider;
        }

        #endregion Konstrukce

        #region Veřejné vlastnosti

        /// <summary>
        /// All active characters in the world (NPCs and players).
        /// </summary>
        public List<CharacterBase> Characters { get; } = new();

        /// <summary>
        /// General store of game objects indexed by type.
        /// Primarily for testing purposes.
        /// </summary>
        public Dictionary<Type, object> Items { get; } = new();

        #endregion Veřejné vlastnosti

        #region Inicializace

        /// <summary>
        /// Initializes the manager — clears state and loads game resources from files.
        /// Called automatically via <c>GameEngineToolsManagerInitializer</c> at host startup.
        /// </summary>
        public void Initialize()
        {
            _log.LogInformation("Initializing CharacterManager...");
            _log.LogInformation("CharacterManager init (logs: {LogsRoot})", _opt.LogsRoot);

            Characters.Clear();
            Items.Clear();

            _log.LogInformation("Loading content...");
            LoadResources();

            Items.Add(typeof(Weapon), _weapons);
            Items.Add(typeof(ArmorPart), _armorParts);

            _log.LogInformation("Loaded");
        }

        #endregion Inicializace

        #region RandomizePerson

        /// <summary>
        /// Generates a random character with the default blueprint parameters.
        /// </summary>
        /// <returns>A newly randomly generated character.</returns>
        public IHuman RandomizePerson()
        {
            var factory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate();
            return factory.Create(hpb);
        }

        /// <summary>
        /// Generates a random character with an age in the given range.
        /// </summary>
        /// <param name="maxAge">Maximum age in years.</param>
        /// <param name="sexBiology">Sex; if null, it is chosen randomly.</param>
        /// <param name="minAge">Minimum age in years (default 0).</param>
        /// <returns>A newly randomly generated character.</returns>
        /// <remarks>
        /// The birth date is chosen randomly within a window derived from the current
        /// game time and the given age range.
        /// </remarks>
        public IHuman RandomizePerson(int maxAge, SexBiology? sexBiology, int minAge = 0)
        {
            var now = _clock.Now;
            var year = now.Year;
            var monthsInYear = WWorld.Spec.Calendar.MonthsInYear(year);

            var rng = _rngFactory.Create(Environment.TickCount);

            var daysInMonth = WWorld.Spec.Calendar.DaysInMonth(year, rng.Next(1, monthsInYear));

            // Date = year ± age, random month and day within the range of the current day
            var minBirth = WDateOnly.New(
                year - maxAge,
                rng.Next(1, monthsInYear),
                rng.Next(1, daysInMonth + 1));

            var maxBirth = WDateOnly.New(
                year - minAge,
                rng.Next(1, monthsInYear),
                rng.Next(1, daysInMonth + 1));

            var factory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate(
                new HumanBlueprintRequest(
                    MinBirthDate: minBirth,
                    MaxBirthDate: maxBirth,
                    Sex: sexBiology));

            return factory.Create(hpb);
        }

        /// <summary>
        /// Generates a character as a potential close relation to the given player —
        /// similar age and the opposite sex.
        /// </summary>
        /// <param name="referenceCharacter">A player/NPC character used as a reference for age and sex.</param>
        /// <returns>A newly randomly generated character.</returns>
        public IHuman RandomizePerson(CharacterBase referenceCharacter)
        {
            var factory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate(
                new HumanBlueprintRequest(
                    MinBirthDate: referenceCharacter.Person.Identity.BirthDate.AddYears(-5),
                    MaxBirthDate: referenceCharacter.Person.Identity.BirthDate.AddYears(5),
                    Sex: referenceCharacter.Person.Biology == SexBiology.Male
                        ? SexBiology.Female
                        : SexBiology.Male));

            return factory.Create(hpb);
        }

        #endregion RandomizePerson

        #region Privátní pomocné metody

        /// <summary>
        /// Loads game resources (weapons, armour) from CSV files.
        /// </summary>
        private void LoadResources()
        {
            _weapons = CsvLoader.Load(FileSystemConstant.SourceFilePath.Weapons,
                v => new Weapon(v[0], Enum.Parse<Weapon.WeaponType>(v[1]),
                    double.Parse(v[2], CultureInfo.InvariantCulture)));

            _armorParts = CsvLoader.Load(FileSystemConstant.SourceFilePath.ArmorParts,
                v => new ArmorPart(v[0], Enum.Parse<ArmorPart.PartType>(v[1]),
                    double.Parse(v[2], CultureInfo.InvariantCulture)));
        }

        #endregion Privátní pomocné metody
    }
}
