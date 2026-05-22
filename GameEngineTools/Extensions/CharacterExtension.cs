// CharacterExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using GameEngineTools.Characters.Core;

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

        public static string ToString(this Surname surname, SexBiology sex)
        {
            if (sex == SexBiology.Male)
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
