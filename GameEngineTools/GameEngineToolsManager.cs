// CharacterManager.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using GameEngineTools.Armory;
    using GameEngineTools.Characters;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.GameObjects;
    using GameEngineTools.Characters.Generation;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Constants;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Core.Calendars;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    public sealed class GameEngineToolsManager : IGameEngineToolsManager
    {
        private readonly ILogger<GameEngineToolsManager> _log;
        private readonly GameEngineToolsManagerOptions _opt;
        private List<ArmorPart> _armorParts = new List<ArmorPart>();
        //private List<Name> _femaleNames = new List<Name>();
        //private List<Name> _maleNames = new List<Name>();
        //private List<Surname> _surnames = new List<Surname>();
        private List<Weapon> _weapons = new List<Weapon>();
        private IServiceProvider _serviceProvider = default!;
        private void LoadResources()
        {
            SourceFile file = new SourceFile();
            file.SetFilenames(FileSystemConstant.SourceFilePath.maleNames, FileSystemConstant.SourceFilePath.femaleNames, FileSystemConstant.SourceFilePath.surnames, FileSystemConstant.SourceFilePath.weapons, FileSystemConstant.SourceFilePath.armorParts);
            //file.Load(out _maleNames, 0);
            //file.Load(out _femaleNames, 1);
            //file.Load(out _surnames, 2);
            file.Load(out _weapons, 3);
            file.Load(out _armorParts, 4);
            //_log.LogInformation("Names loaded: male={Male}, female={Female}, surnames={Surnames}", _maleNames.Count, _femaleNames.Count, _surnames.Count);
        }

        public GameEngineToolsManager(
            IClock clock,
            IOptions<GameEngineToolsManagerOptions> opt,
            ILogger<GameEngineToolsManager> log,
            IServiceProvider serviceProvider)
        {
            _opt = opt.Value;
            _log = log;
            _clock = clock;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// For test purposes!
        /// </summary>
        public Dictionary<Type, object> Items { get; } = new Dictionary<Type, object>();

        public List<CharacterBase> NPPCs { get; } = new List<CharacterBase>();

        //public IServiceProvider ServiceProvider { get; private set; } = default!;

        private IClock _clock;

        public void Initialize()
        {
            _log.LogInformation("Initializing CharacterManager...");
            _log.LogInformation("CharacterManager init (logs: {0})", _opt.LogsRoot);
            _log.LogInformation("Initializing...");
            CharacterBase.People.Clear();
            NPPCs.Clear();
            Items.Clear();
            _log.LogInformation("Initialized");
            _log.LogInformation("Loading content...");
            LoadResources();
            Items.Add(typeof(Weapon), _weapons);
            Items.Add(typeof(ArmorPart), _armorParts);
            _log.LogInformation("Loaded");
        }

        public IHuman RandomizePerson()
        {
            var characterFactory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate();
            return characterFactory.Create(hpb);
        }

        public IHuman RandomizePerson(int minAge = 0, int maxAge = 100)
        {
            var now = _clock.Now;

            var rng = _serviceProvider.GetRequiredService<IRandomSource>().Create();

            var minBirth = WDateOnly.FromDateTime(new WDateTime(now.Year - maxAge, rng.Next(0, (WDateTime.Spec.Calendar as FixedMonthsCalendar)!.MonthsInYear), rng.Next(1, now.Day), now.Hour, now.Minute, now.Second));
            var maxBirth = WDateOnly.FromDateTime(new WDateTime(now.Year - minAge, rng.Next(0, (WDateTime.Spec.Calendar as FixedMonthsCalendar)!.MonthsInYear), rng.Next(1, now.Day), now.Hour, now.Minute, now.Second));
            var characterFactory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate(
                new HumanBlueprintRequest(
                    MinBirthDate: minBirth,
                    MaxBirthDate: maxBirth));
            return characterFactory.Create(hpb);
        }

        public IHuman RandomizePerson(PC player)
        {
            var characterFactory = _serviceProvider.GetRequiredService<IHumanFactory>();
            var hpb = _serviceProvider.GetRequiredService<IHumanBlueprintGenerator>().Generate(
            new HumanBlueprintRequest(
                MinBirthDate: player.Person.Identity.BirthDate.AddYears(-5),
                MaxBirthDate: player.Person.Identity.BirthDate.AddYears(5),
                Sex: player.Person.Biology == SexBiology.Male ? SexBiology.Female : SexBiology.Male));

            return characterFactory.Create(hpb);
        }
    }
}
