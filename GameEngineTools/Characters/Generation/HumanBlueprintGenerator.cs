// HumanBlueprintGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using GameEngineTools.Characters.Core;
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
        int? Seed = null);

    /// <summary>
    /// Specifikace generátoru postav — váhy pohlaví a výchozí věkový rozsah.
    /// Registruj do DI jako singleton přes
    /// <see cref="ServiceCollectionExtensions.AddCharacterGeneration(Microsoft.Extensions.DependencyInjection.IServiceCollection, Func{IServiceProvider, HumanBlueprintSpec})"/>.
    /// </summary>
    public sealed record HumanBlueprintSpec(
        (double Female, double Male, double Intersex, double Unknown) SexWeights,
        WDateOnly DefaultMinBirthDate,
        WDateOnly DefaultMaxBirthDate)
    {
        /// <summary>
        /// Sestaví výchozí spec z aktuálního data ve světě.
        /// Výchozí věkový rozsah je 0–100 let odvozených z délky roku v herním kalendáři.
        /// </summary>
        /// <param name="now">Aktuální datum ve světě (referenční bod pro výpočet věku).</param>
        /// <param name="ctx">
        /// Kontext světového času — potřebný pro délku roku v kalendáři.
        /// Nahrazuje odstraněný globální <c>WDateTime.Spec</c>.
        /// </param>
        /// <param name="minAgeYears">Minimální věk v letech (výchozí 0).</param>
        /// <param name="maxAgeYears">Maximální věk v letech (výchozí 100).</param>
        /// <returns>Výchozí <see cref="HumanBlueprintSpec"/>.</returns>
        /// <remarks>
        /// Typické použití v DI registraci:
        /// <code>
        /// s.AddCharacterGeneration(sp =>
        /// {
        ///     var ctx = sp.GetRequiredService&lt;WorldTimeContext&gt;();
        ///     return HumanBlueprintSpec.Default(ctx.GetDate(ctx.Now()), ctx);
        /// });
        /// </code>
        /// </remarks>
        public static HumanBlueprintSpec Default(
            WDateOnly now,
            int minAgeYears = 0,
            int maxAgeYears = 100)
        {
            var daysInYear = WWorld.Spec.Calendar.DaysInYear(now.Year);
            var minAgeDays = minAgeYears * daysInYear;
            var maxAgeDays = maxAgeYears * daysInYear;

            WDateOnly minBirth;
            try
            {
                minBirth = new WDateOnly(now.DayIndex - maxAgeDays);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Datum by šlo před epochu — zarovnáme na 30 dní dopředu od "teď"
                // TODO: nahradit lunárním výpočtem
                minBirth = new WDateOnly(now.DayIndex + 30);
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

        #endregion

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

        #endregion

        #region IIdentityGenerator

        /// <inheritdoc/>
        public Identity Generate(SexBiology sex, WDateOnly birthDate, IRandomSource rng)
        {
            var first = sex == SexBiology.Female ? Pick(_femaleNames, rng) : Pick(_maleNames, rng);
            var surname = Pick(_surnames, rng);
            return new Identity(first, surname, birthDate);
        }

        #endregion

        #region Privátní pomocné metody

        private static T Pick<T>(T[] values, IRandomSource rng)
        {
            if (values.Length == 0)
            {
                throw new InvalidOperationException("No values to pick from.");
            }

            return values[rng.Next(0, values.Length)];
        }

        #endregion
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
        private readonly HumanBlueprintSpec _spec;

        #endregion

        #region Konstrukce

        /// <summary>
        /// Inicializuje generátor se všemi potřebnými závislostmi.
        /// </summary>
        public HumanBlueprintGenerator(
            IRandomSourceFactory rngFactory,
            IPersonalityGenerator personalityGenerator,
            IIdentityGenerator identityGenerator,
            IAppearanceGenerator appearanceGenerator,
            HumanBlueprintSpec spec)
        {
            _rngFactory = rngFactory;
            _personalityGenerator = personalityGenerator;
            _identityGenerator = identityGenerator;
            _appearanceGenerator = appearanceGenerator;
            _spec = spec;
        }

        #endregion

        #region IHumanBlueprintGenerator

        /// <inheritdoc/>
        public HumanBlueprint Generate(HumanBlueprintRequest? request = null)
        {
            request ??= new HumanBlueprintRequest();

            var seed = request.Seed ?? Environment.TickCount;
            var rng = _rngFactory.Create(seed);

            var sex = request.Sex ?? PickSex(_spec.SexWeights, rng);
            var birthDate = PickBirthDate(
                request.MinBirthDate ?? _spec.DefaultMinBirthDate,
                request.MaxBirthDate ?? _spec.DefaultMaxBirthDate,
                rng);

            var identity = _identityGenerator.Generate(sex, birthDate, rng);
            var personality = _personalityGenerator.Generate(
                rng.Next(int.MinValue, int.MaxValue),
                request.PersonalityHints,
                request.PersonalitySpec);

            var id = new HumanId(Guid.NewGuid());
            var runtimeSeed = request.Seed ?? DeriveSeedFromId(id);
            var appearance = _appearanceGenerator.Generate(sex, seed);

            return new HumanBlueprint(id, identity, sex, personality, appearance, runtimeSeed);
        }

        #endregion

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

        #endregion
    }
}
