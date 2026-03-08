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
    /// Hlavní správce herního světa — inicializuje zdroje, spravuje postavy
    /// a poskytuje factory metody pro generování nových postav.
    /// </summary>
    public sealed class GameEngineToolsManager : IGameEngineToolsManager
    {
        #region Soukromá pole

        private readonly ILogger<GameEngineToolsManager> _log;
        private readonly GameEngineToolsManagerOptions _opt;
        private readonly IClock _clock;
        private readonly WorldTimeContext _wtctx;
        private readonly IRandomSourceFactory _rngFactory;
        private readonly IServiceProvider _serviceProvider;

        private List<ArmorPart> _armorParts = new();
        private List<Weapon> _weapons = new();

        #endregion

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
            WorldTimeContext wtctx,
            IRandomSourceFactory rngFactory,
            IOptions<GameEngineToolsManagerOptions> opt,
            ILogger<GameEngineToolsManager> log,
            IServiceProvider serviceProvider)
        {
            _clock = clock;
            _wtctx = wtctx;
            _rngFactory = rngFactory;
            _opt = opt.Value;
            _log = log;
            _serviceProvider = serviceProvider;
        }

        #endregion

        #region Veřejné vlastnosti

        /// <summary>
        /// Všechny aktivní postavy ve světě (NPC i hráčské).
        /// </summary>
        public List<CharacterBase> NPPCs { get; } = new();

        /// <summary>
        /// Obecné úložiště herních objektů indexované typem.
        /// Primárně pro testovací účely.
        /// </summary>
        public Dictionary<Type, object> Items { get; } = new();

        #endregion

        #region Inicializace

        /// <summary>
        /// Inicializuje manager — vyčistí stav a načte herní zdroje ze souborů.
        /// Voláno automaticky přes <see cref="GameEngineToolsManagerInitializer"/> při startu hostu.
        /// </summary>
        public void Initialize()
        {
            _log.LogInformation("Initializing CharacterManager...");
            _log.LogInformation("CharacterManager init (logs: {LogsRoot})", _opt.LogsRoot);

            NPPCs.Clear();
            Items.Clear();

            _log.LogInformation("Loading content...");
            LoadResources();

            Items.Add(typeof(Weapon), _weapons);
            Items.Add(typeof(ArmorPart), _armorParts);

            _log.LogInformation("Loaded");
        }

        #endregion

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
        /// <param name="minAge">Minimální věk v letech (výchozí 0).</param>
        /// <param name="maxAge">Maximální věk v letech (výchozí 100).</param>
        /// <returns>Nová náhodně vygenerovaná postava.</returns>
        /// <remarks>
        /// Datum narození se náhodně volí uvnitř okna odvozeného od aktuálního
        /// herního času a zadaného věkového rozsahu.
        /// </remarks>
        public IHuman RandomizePerson(int minAge = 0, int maxAge = 100)
        {
            // Rozkládáme "teď" jednou — GetParts volá kalendář, nechceme to opakovat
            var now = _clock.Now.Bind(_wtctx);
            var (year, _, day, hour, minute, second, _) = _wtctx.GetParts(now);
            var monthsInYear = _wtctx.Spec.Calendar.MonthsInYear(year);

            var rng = _rngFactory.Create(Environment.TickCount);

            // Datum = rok ± věk, náhodný měsíc a den v rozsahu aktuálního dne
            var minBirth = _wtctx.GetDate(_wtctx.Create(
                year - maxAge,
                rng.Next(1, monthsInYear),
                rng.Next(1, day),
                hour, minute, second));

            var maxBirth = _wtctx.GetDate(_wtctx.Create(
                year - minAge,
                rng.Next(1, monthsInYear),
                rng.Next(1, day),
                hour, minute, second));

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
        /// <param name="player">Hráčská postava jako reference pro věk a pohlaví.</param>
        /// <returns>Nová náhodně vygenerovaná postava.</returns>
        public IHuman RandomizePerson(PC player)
        {
            // BirthDate je WDateOnly → přivážeme na wtctx a voláme AddYears přímo
            var birth = player.Person.Identity.BirthDate.Bind(_wtctx);

            var factory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate(
                new HumanBlueprintRequest(
                    MinBirthDate: birth.AddYears(-5),
                    MaxBirthDate: birth.AddYears(5),
                    Sex: player.Person.Biology == SexBiology.Male
                        ? SexBiology.Female
                        : SexBiology.Male));

            return factory.Create(hpb);
        }

        #endregion

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

        #endregion
    }
}
