// HumanBlueprintGenerator.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Generation
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel.DataAnnotations;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.Characters.Hosting;
    using GameEngineTools.Characters.Hosting.Defaults;
    using GameEngineTools.Constants;
    using GameEngineTools.FileSystem;
    using GameEngineTools.World.Utils.Time;

    public sealed record HumanBlueprintRequest(SexBiology? Sex = null, WDateOnly? MinBirthDate = null, WDateOnly? MaxBirthDate = null, PersonalityHints? PersonalityHints = null, PersonalitySpec? PersonalitySpec = null, int? Seed = null);

    public sealed record HumanBlueprintSpec((double Female, double Male, double Intersex, double Unknown) SexWeights, WDateOnly DefaultMinBirthDate, WDateOnly DefaultMaxBirthDate)
    {
        public static HumanBlueprintSpec Default(WDateOnly now)
        {
            var minAgeDays = 0 * WDateTime.Spec.Calendar.DaysInYear(now.Year);
            var maxAgeDays = 100 * WDateTime.Spec.Calendar.DaysInYear(now.Year);

            var minBirth = now.AddDays(-maxAgeDays);
            var maxBirth = now.AddDays(-minAgeDays);

            return new HumanBlueprintSpec(
                (Female: 0.49, Male: 0.49, Intersex: 0.01, Unknown: 0.01),
                DefaultMinBirthDate: minBirth,
                DefaultMaxBirthDate: maxBirth
            );
        }
    }
    public interface IHumanBlueprintGenerator
    {
        HumanBlueprint Generate(HumanBlueprintRequest? request = null);
    }

    public interface IIdentityGenerator
    {
        Identity Generate(SexBiology sex, WDateOnly birthDate, IRandomSource rng);
    }

    public sealed class SimpleIdentityGenerator : IIdentityGenerator
    {
        private readonly Name[] _femaleNames;
        private readonly Name[] _maleNames;
        private readonly Surname[] _surnames;

        public SimpleIdentityGenerator()
        {
            var sourceFile = new SourceFile();
            sourceFile.SetFilenames(
                FileSystemConstant.SourceFilePath.femaleNames,
                FileSystemConstant.SourceFilePath.maleNames,
                FileSystemConstant.SourceFilePath.surnames);
            var femaleNames = new List<Name>();
            var maleNames = new List<Name>();
            var surnames = new List<Surname>();
            sourceFile.Load<Name>(out femaleNames, 0);
            sourceFile.Load<Name>(out maleNames, 1);
            sourceFile.Load<Surname>(out surnames, 2);
            _femaleNames = femaleNames.ToArray();
            _maleNames = maleNames.ToArray();
            _surnames = surnames.ToArray();
        }

        public Identity Generate(SexBiology sex, WDateOnly birthDate, IRandomSource rng)
        {
            var first = sex == SexBiology.Female
                ? Pick(_femaleNames, rng)
                : Pick(_maleNames, rng);

            var surname = Pick(_surnames, rng);

            return new Identity(first, surname, birthDate);

            static T Pick<T>(T[] values, IRandomSource rng)
            {
                if (values.Length == 0)
                {
                    throw new InvalidOperationException("No values to pick from.");
                }

                var i = rng.Next(0, values.Length);
                return values[i];
            }
        }
    }

    public sealed class HumanBlueprintGenerator : IHumanBlueprintGenerator
    {
        private readonly IRandomSourceFactory _rngFactory;
        private readonly IPersonalityGenerator _personalityGenerator;
        private readonly IIdentityGenerator _identityGenerator;
        private readonly HumanBlueprintSpec _spec;
        public HumanBlueprintGenerator(IRandomSourceFactory rngFactory, IPersonalityGenerator personalityGenerator, IIdentityGenerator identityGenerator, HumanBlueprintSpec spec)
        {
            _rngFactory = rngFactory;
            _personalityGenerator = personalityGenerator;
            _identityGenerator = identityGenerator;
            _spec = spec;
        }

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

            var personality = _personalityGenerator.Generate(rng.Next(int.MinValue, int.MaxValue), request.PersonalityHints, request.PersonalitySpec);

            var id = new HumanId(Guid.NewGuid());
            var runtimeSeed = request.Seed ?? DeriveSeedFromId(id);

            return new HumanBlueprint(id, identity, sex, personality, runtimeSeed);
        }

        private static SexBiology PickSex((double Female, double Male, double Intersex, double Unknown) w, IRandomSource rng)
        {
            var total = w.Female + w.Male + w.Intersex + w.Unknown;
            if (total <= 0) throw new InvalidOperationException("Invalid sex weights.");
            var r = rng.NextUnit() * total;

            if (r < w.Female) return SexBiology.Female;
            r -= w.Female;
            if (r < w.Male) return SexBiology.Male;
            r -= w.Male;
            if (r < w.Intersex) return SexBiology.Intersex;
            return SexBiology.Unknown;
        }

        private static WDateOnly PickBirthDate(WDateOnly minDate, WDateOnly maxDate, IRandomSource rng)
        {
            var minIdx = minDate.DayIndex;
            var maxIdx = maxDate.DayIndex;

            if (maxIdx < minIdx) throw new ArgumentException("MaxBirthDate < MinBirthDate");

            var span = maxIdx - minIdx + 1;
            var offset = (long)(rng.NextUnit() * span);
            var dayIdx = minIdx + offset;
            return new WDateOnly(dayIdx);
        }

        private static int DeriveSeedFromId(HumanId id)
        {
            // Deterministický seed z Guidu (stabilní, ale rozumně rozptýlený)
            var g = id.Value;
            var bytes = g.ToByteArray();
            int hash = 17;
            foreach (var b in bytes)
            {
                hash = hash * 31 + b;
            }
            return hash;
        }
    }
}
