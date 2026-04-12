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
    using GameEngineTools.World.Location;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Hlavní správce herního světa — inicializuje zdroje, spravuje postavy
    /// a poskytuje factory metody pro generování nových postav.
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
        /// Inicializuje manager se všemi závislostmi přes DI.
        /// </summary>
        /// <param name="clock">Zdroj aktuálního herního času (pro RandomizePerson).</param>
        /// <param name="wtctx">
        /// Kontext světového času — nahrazuje odstraněný globální <c>WDateTime.Spec</c>.
        /// Používá se pro dekompozici <see cref="WDateTime"/> a aritmetiku nad
        /// <see cref="WDateOnly"/>.
        /// </param>
        /// <param name="rngFactory">Factory pro generátory náhodných čísel.</param>
        /// <param name="opt">Konfigurační volby manageru (logy, adresáře).</param>
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
        /// Všechny aktivní postavy ve světě (NPC i hráčské).
        /// </summary>
        public List<CharacterBase> Characters { get; } = new();

        /// <summary>
        /// Obecné úložiště herních objektů indexované typem.
        /// Primárně pro testovací účely.
        /// </summary>
        public Dictionary<Type, object> Items { get; } = new();

        #endregion Veřejné vlastnosti

        #region Inicializace

        /// <summary>
        /// Inicializuje manager — vyčistí stav a načte herní zdroje ze souborů.
        /// Voláno automaticky přes <see cref="GameEngineToolsManagerInitializer"/> při startu hostu.
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
        /// Vygeneruje náhodnou postavu s výchozími parametry blueprintu.
        /// </summary>
        /// <returns>Nová náhodně vygenerovaná postava.</returns>
        public IHuman RandomizePerson()
        {
            var factory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate();
            return factory.Create(hpb);
        }

        /// <summary>
        /// Vygeneruje náhodnou postavu s věkem v zadaném rozsahu.
        /// </summary>
        /// <param name="maxAge">Maximální věk v letech.</param>
        /// <param name="minAge">Minimální věk v letech (výchozí 0).</param>
        /// <returns>Nová náhodně vygenerovaná postava.</returns>
        /// <remarks>
        /// Datum narození se náhodně volí uvnitř okna odvozeného od aktuálního
        /// herního času a zadaného věkového rozsahu.
        /// </remarks>
        public IHuman RandomizePerson(int maxAge, int minAge = 0)
        {
            var now = _clock.Now;
            var year = now.Year;
            var monthsInYear = WWorld.Spec.Calendar.MonthsInYear(year);

            var rng = _rngFactory.Create(Environment.TickCount);

            var daysInMonth = WWorld.Spec.Calendar.DaysInMonth(year, rng.Next(1, monthsInYear));

            // Datum = rok ± věk, náhodný měsíc a den v rozsahu aktuálního dne
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
                    MaxBirthDate: maxBirth));

            return factory.Create(hpb);
        }

        /// <summary>
        /// Vygeneruje postavu jako potenciálního blízkého k zadanému hráči —
        /// podobný věk a opačné pohlaví.
        /// </summary>
        /// <param name="referenceCharacter">Hráčská/NPC postava jako reference pro věk a pohlaví.</param>
        /// <returns>Nová náhodně vygenerovaná postava.</returns>
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
        /// Načte herní zdroje (zbraně, brnění) ze souborů CSV.
        /// </summary>
        private void LoadResources()
        {
            _weapons = CsvLoader.Load(FileSystemConstant.SourceFilePath.weapons,
                v => new Weapon(v[0], Enum.Parse<Weapon.WeaponType>(v[1]),
                    double.Parse(v[2], CultureInfo.InvariantCulture)));

            _armorParts = CsvLoader.Load(FileSystemConstant.SourceFilePath.armorParts,
                v => new ArmorPart(v[0], Enum.Parse<ArmorPart.PartType>(v[1]),
                    double.Parse(v[2], CultureInfo.InvariantCulture)));
        }

        #endregion Privátní pomocné metody
    }
}
