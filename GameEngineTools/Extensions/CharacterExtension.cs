// CharacterExtension.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Extensions
{
    using GameEngineTools.Characters.Core;

    /// <summary>Extension helpers for working with characters.</summary>
    public static class CharacterExtension
    {
        /// <summary>Finds the character whose id matches the given value.</summary>
        /// <param name="people">The characters to search.</param>
        /// <param name="dna">The identifier to match.</param>
        /// <returns>The matching character.</returns>
        /// <exception cref="Exception">Thrown when no match is found.</exception>
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

        /// <summary>Returns the sex-appropriate form of a surname.</summary>
        /// <param name="surname">The surname.</param>
        /// <param name="sex">The biological sex selecting the form.</param>
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
