// CharacterExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Utils.Time;

    public static class CharacterExtension
    {
        public static IHuman FindByDNA(this IEnumerable<IHuman> people, Guid dna)
        {
            foreach (var person in people)
            {
                if (person.Id.Value == dna)
                {
                    return person;
                }
            }
            throw new Exception("No person was found!");
        }

        public static string ToString(this Surname surname, GenusType genus)
        {
            if (genus == GenusType.Male)
            {
                return surname.Male;
            }
            else
            {
                return surname.Female;
            }
        }
    }
}
