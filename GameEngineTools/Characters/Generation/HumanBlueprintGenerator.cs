// HumanBlueprintGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using System.Collections.Generic;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Engines.Schedule;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    // ─────────────────────────────────────────────────────────────────────────
    //  Request & Spec
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Vstupní parametry pro generování blueprintu jedné postavy.
    /// Všechna pole jsou volitelná — pokud nejsou zadána, použijí se výchozí
    /// hodnoty z <see cref="HumanBlueprintSpec"/>.
    /// </summary>
    public sealed record HumanBlueprintRequest(
        SexBiology? Sex = null,
        WDateOnly? MinBirthDate = null,
        WDateOnly? MaxBirthDate = null,
        PersonalityHints? PersonalityHints = null,
        PersonalitySpec? PersonalitySpec = null,
        int? Seed = null,
        /// <summary>
        /// Explicitně zadané povolání (ID z <see cref="OccupationIds"/> nebo vlastní).
        /// Pokud je <c>null</c>, povolání se vybere losem z
        /// <see cref="HumanBlueprintSpec.OccupationWeights"/> s přihlédnutím k věkové skupině.
        /// </summary>
        string? Occupation = null);

    /// <summary>
    /// Specifikace generátoru postav — váhy pohlaví, věkový rozsah a váhy povolání.
    /// Registruj do DI jako singleton přes
    /// <see cref="ServiceCollectionExtensions.AddCharacterGeneration(Microsoft.Extensions.DependencyInjection.IServiceCollection, Func{IServiceProvider, HumanBlueprintSpec})"/>.
    /// </summary>
    public sealed record HumanBlueprintSpec(
        (double Female, double Male, double Intersex, double Unknown) SexWeights,
        WDateOnly DefaultMinBirthDate,
        WDateOnly DefaultMaxBirthDate,
        /// <summary>
        /// Váhy povolání použité při generování, pokud <see cref="HumanBlueprintRequest.Occupation"/>
        /// není explicitně zadáno. Klíče jsou occupation ID (viz <see cref="OccupationIds"/>);
        /// prázdný řetězec nebo <see cref="OccupationIds.None"/> znamená žádné povolání.
        /// Hodnoty nemusí dávat dohromady 1 — generátor je normalizuje.
        /// </summary>
        IReadOnlyDictionary<string, double>? OccupationWeights = null)
    {
        /// <summary>
        /// Výchozí váhy povolání — hrubá distribuce středověké populace.
        /// Klíč <c>""</c> (<see cref="OccupationIds.None"/>) určuje pravděpodobnost
        /// postavy bez povolání.
        /// </summary>
        public static IReadOnlyDictionary<string, double> DefaultOccupationWeights { get; } =
            new Dictionary<string, double>
            {
                [OccupationIds.None] = 0.10,
                [OccupationIds.Craftsperson] = 0.15,
                [OccupationIds.Merchant] = 0.10,
                [OccupationIds.Scholar] = 0.05,
                [OccupationIds.Farmer] = 0.25,
                [OccupationIds.Guard] = 0.08,
                [OccupationIds.Healer] = 0.05,
                [OccupationIds.Artist] = 0.07,
                [OccupationIds.Laborer] = 0.15,
            };

        /// <summary>Builds a default blueprint spec with a uniform age range over the given window.</summary>
        /// <param name="now">Current game date (used to derive birth-date ranges).</param>
        /// <param name="minAgeYears">Minimum generated age in years.</param>
        /// <param name="maxAgeYears">Maximum generated age in years.</param>
        public static HumanBlueprintSpec Default(
            WDateOnly now,
            int minAgeYears = 0,
            int maxAgeYears = 100)
        {
            var daysInYear = WWorld.Spec.Calendar.DaysInYear(now.Year);
            var minAgeDays = (minAgeYears == 0) ? 1 : minAgeYears * daysInYear;
            var maxAgeDays = maxAgeYears * daysInYear;

            WDateOnly minBirth;
            try
            {
                minBirth = new WDateOnly(now.DayIndex - maxAgeDays);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Datum by šlo před epochu — zarovnáme na 30 dní dozadu od "teď"
                // TODO: nahradit lunárním výpočtem
                minBirth = new WDateOnly(now.DayIndex - 30);
            }

            var maxBirth = new WDateOnly(now.DayIndex - minAgeDays);

            return new HumanBlueprintSpec(
                SexWeights: (Female: 0.49, Male: 0.49, Intersex: 0.01, Unknown: 0.01),
                DefaultMinBirthDate: minBirth,
                DefaultMaxBirthDate: maxBirth);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Interfaces
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Generátor blueprintů postav.</summary>
    public interface IHumanBlueprintGenerator
    {
        /// <summary>
        /// Vygeneruje blueprint postavy podle volitelného requestu.
        /// Pokud request není zadán, použijí se výchozí hodnoty ze spec.
        /// </summary>
        /// <param name="request">Volitelné parametry generování (pohlaví, věk, seed…).</param>
        HumanBlueprint Generate(HumanBlueprintRequest? request = null);
    }

    /// <summary>Generátor identit postav (jméno, příjmení, datum narození).</summary>
    public interface IIdentityGenerator
    {
        /// <summary>
        /// Vygeneruje identitu postavy.
        /// </summary>
        /// <param name="sex">Pohlaví (ovlivňuje výběr jména).</param>
        /// <param name="birthDate">Datum narození.</param>
        /// <param name="rng">Zdroj náhodných čísel.</param>
        Identity Generate(SexBiology sex, WDateOnly birthDate, IRandomSource rng);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  SimpleIdentityGenerator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Jednoduchý generátor identit — náhodně vybírá z předem načtených seznamů jmen a příjmení.
    /// </summary>
    public sealed class SimpleIdentityGenerator : IIdentityGenerator
    {
        #region Soukromá pole

        private readonly Name[] _femaleNames;
        private readonly Name[] _maleNames;
        private readonly Surname[] _surnames;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje generátor se seznamy jmen a příjmení.
        /// </summary>
        /// <param name="femaleNames">Ženská jména.</param>
        /// <param name="maleNames">Mužská jména.</param>
        /// <param name="surnames">Příjmení (s mužskou a ženskou variantou).</param>
        public SimpleIdentityGenerator(Name[] femaleNames, Name[] maleNames, Surname[] surnames)
        {
            _femaleNames = femaleNames;
            _maleNames = maleNames;
            _surnames = surnames;
        }

        #endregion Konstrukce

        #region IIdentityGenerator

        /// <inheritdoc/>
        public Identity Generate(SexBiology sex, WDateOnly birthDate, IRandomSource rng)
        {
            var first = sex == SexBiology.Female ? Pick(_femaleNames, rng) : Pick(_maleNames, rng);
            var surname = Pick(_surnames, rng);
            return new Identity(first, surname, birthDate);
        }

        #endregion IIdentityGenerator

        #region Privátní pomocné metody

        private static T Pick<T>(T[] values, IRandomSource rng)
        {
            if (values.Length == 0)
            {
                throw new InvalidOperationException("No values to pick from.");
            }

            return values[rng.Next(0, values.Length)];
        }

        #endregion Privátní pomocné metody
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  HumanBlueprintGenerator
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Generátor blueprintů postav. Kombinuje identity, osobnost a vzhled
    /// do jednoho <see cref="HumanBlueprint"/>.
    /// </summary>
    public sealed class HumanBlueprintGenerator : IHumanBlueprintGenerator
    {
        #region Soukromá pole

        private readonly IRandomSourceFactory _rngFactory;
        private readonly IPersonalityGenerator _personalityGenerator;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly IAppearanceGenerator _appearanceGenerator;
        private readonly IAttractionProfileGenerator _attractionProfileGenerator;
        private readonly HumanBlueprintSpec _spec;

        #endregion Soukromá pole

        #region Konstrukce

        /// <summary>
        /// Inicializuje generátor se všemi potřebnými závislostmi.
        /// </summary>
        public HumanBlueprintGenerator(
            IRandomSourceFactory rngFactory,
            IPersonalityGenerator personalityGenerator,
            IIdentityGenerator identityGenerator,
            IAppearanceGenerator appearanceGenerator,
            IAttractionProfileGenerator attractionProfileGenerator,
            HumanBlueprintSpec spec)
        {
            _rngFactory = rngFactory;
            _personalityGenerator = personalityGenerator;
            _identityGenerator = identityGenerator;
            _appearanceGenerator = appearanceGenerator;
            _attractionProfileGenerator = attractionProfileGenerator;
            _spec = spec;
        }

        #endregion Konstrukce

        #region IHumanBlueprintGenerator

        /// <inheritdoc/>
        public HumanBlueprint Generate(HumanBlueprintRequest? request = null)
        {
            request ??= new HumanBlueprintRequest();

            var id = new HumanId(Guid.NewGuid());
            var runtimeSeed = request.Seed ?? DeriveSeedFromId(id);

            var seed = request.Seed ?? Environment.TickCount;
            var rng = _rngFactory.Create(runtimeSeed);

            var sex = request.Sex ?? PickSex(_spec.SexWeights, rng);
            var birthDate = PickBirthDate(
                request.MinBirthDate ?? _spec.DefaultMinBirthDate,
                request.MaxBirthDate ?? _spec.DefaultMaxBirthDate,
                rng);

            var identity = _identityGenerator.Generate(sex, birthDate, rng);

            var today = WDateOnly.Today;
            var ageYears = today.Year - birthDate.Year;
            if (today.Month < birthDate.Month ||
                (today.Month == birthDate.Month && today.Day < birthDate.Day))
            {
                ageYears--;
            }

            var stadium = StadiumResolver.Resolve(Math.Max(0, ageYears));

            var hints = request.PersonalityHints ?? PersonalityHints.ForStadium(stadium);
            var spec = request.PersonalitySpec ?? PersonalitySpec.ForStadium(stadium);

            var personality = _personalityGenerator.Generate(rng.Next(int.MinValue, int.MaxValue), hints, spec);

            var geneticBlueprint = _appearanceGenerator.GenerateBlueprint(sex, runtimeSeed);
            var attractionProfile = _attractionProfileGenerator.Generate(sex, rng);

            var occupation = request.Occupation
                ?? PickOccupation(_spec.OccupationWeights ?? HumanBlueprintSpec.DefaultOccupationWeights, rng, stadium);

            return new HumanBlueprint(id, identity, sex, personality, geneticBlueprint, attractionProfile, runtimeSeed, occupation);
        }

        #endregion IHumanBlueprintGenerator

        #region Privátní pomocné metody

        private static SexBiology PickSex(
            (double Female, double Male, double Intersex, double Unknown) w,
            IRandomSource rng)
        {
            var total = w.Female + w.Male + w.Intersex + w.Unknown;
            if (total <= 0)
            {
                throw new InvalidOperationException("Invalid sex weights.");
            }

            var r = rng.NextUnit() * total;
            if (r < w.Female)
            {
                return SexBiology.Female;
            }

            r -= w.Female;
            if (r < w.Male)
            {
                return SexBiology.Male;
            }

            r -= w.Male;
            if (r < w.Intersex)
            {
                return SexBiology.Intersex;
            }

            return SexBiology.Unknown;
        }

        private static WDateOnly PickBirthDate(WDateOnly minDate, WDateOnly maxDate, IRandomSource rng)
        {
            var minIdx = minDate.DayIndex;
            var maxIdx = maxDate.DayIndex;

            if (maxIdx < minIdx)
            {
                throw new ArgumentException("MaxBirthDate < MinBirthDate");
            }

            var span = maxIdx - minIdx + 1;
            var offset = (long)(rng.NextUnit() * span);
            return new WDateOnly(minIdx + offset);
        }

        /// <summary>
        /// Vybere povolání váhovým losem s přihlédnutím k věkové skupině postavy.
        /// Vrátí <c>null</c> (= žádné povolání) pro mladé postavy a
        /// při výběru prázdného klíče <c>""</c> (<see cref="OccupationIds.None"/>).
        /// </summary>
        /// <remarks>
        /// Stadium modifikátory:
        /// <list type="bullet">
        ///   <item><b>Baby / Child / Teenager</b> — vždy null (nelze pracovat).</item>
        ///   <item><b>Adult</b> — plná distribuce dle vah.</item>
        ///   <item><b>MidAged</b> — built-in fyzické práce mají poloviční váhu.</item>
        ///   <item><b>Old</b> — z built-in povolání povolena jen sedavá + null; ostatní built-in zeroed.</item>
        /// </list>
        /// Neznámá (custom) povolání nejsou modifikována stadiem — odpovědnost vývojáře.
        /// </remarks>
        private static string? PickOccupation(IReadOnlyDictionary<string, double> weights, IRandomSource rng, StadiumType stadium)
        {
            // Mladé postavy nepracují
            if (stadium is StadiumType.Baby or StadiumType.Child or StadiumType.Teenager)
                return null;

            // Sestavíme efektivní váhy s stadium modifikátory
            var effective = new List<(string id, double weight)>(weights.Count);
            foreach (var (id, w) in weights)
            {
                var effectiveWeight = w;

                if (stadium == StadiumType.MidAged)
                {
                    if (OccupationIds.PhysicalOccupations.Contains(id))
                        effectiveWeight *= 0.5;
                }
                else if (stadium == StadiumType.Old)
                {
                    // Pro built-in povolání: pouze sedavá a "none" (prázdný řetězec)
                    if (OccupationIds.All.Contains(id) && !OccupationIds.SedentaryOccupations.Contains(id))
                        effectiveWeight = 0.0;
                    // Custom (neznámá) povolání nejsou modifikována
                }

                if (effectiveWeight > 0.0)
                    effective.Add((id, effectiveWeight));
            }

            var total = 0.0;
            foreach (var (_, w) in effective) total += w;

            if (total <= 0.0)
                return null;

            var r = rng.NextUnit() * total;
            foreach (var (id, w) in effective)
            {
                r -= w;
                if (r <= 0.0)
                    return string.IsNullOrEmpty(id) ? null : id;
            }

            return null;
        }

        /// <summary>
        /// Deterministický seed z Guidu — stabilní, ale rozumně rozptýlený.
        /// </summary>
        private static int DeriveSeedFromId(HumanId id)
        {
            var bytes = id.Value.ToByteArray();
            int hash = 17;
            foreach (var b in bytes)
            {
                hash = hash * 31 + b;
            }

            return hash;
        }

        #endregion Privátní pomocné metody
    }
}
