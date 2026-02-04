using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grammar.Helpers;

namespace Grammar.Services
{
    public class SofteningService : ISofteningService
    {
        private static readonly Dictionary<string, string> SofteningMap = new()
        {
            { "k", "c" },
            { "h", "z" },
            { "ch", "š" }, // nutné ošetřit jako dvoupísmenné
            { "d", "ď" },
            { "t", "ť" },
            { "n", "ň" },
            { "c", "č" }
        };

        private static readonly HashSet<string> SofteningSuffixes = new()
        {
            "i", "í", "e", "ě", "ím", "ích", "emi", "ům", "ovi", "ami", "ích"
        };

        private static readonly Dictionary<string, string> ReverseMap = SofteningMap.ToDictionary(kv => kv.Value, kv => kv.Key);

        public string ApplySofteningIfNeeded(string baseWord, string suffix)
        {
            if (string.IsNullOrEmpty(baseWord) || string.IsNullOrEmpty(suffix))
                return baseWord;

            if (!SofteningSuffixes.Contains(suffix))
                return baseWord;

            if (baseWord.EndsWith("ch", StringComparison.OrdinalIgnoreCase))
                return baseWord[..^2] + "š";

            var last = baseWord[^1..];
            if (SofteningMap.TryGetValue(last, out var softened))
                return baseWord[..^1] + softened;

            return baseWord;
        }

        public string RevertSoftening(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            if (word.EndsWith("š", StringComparison.OrdinalIgnoreCase))
            {
                // speciální případ ch → š
                return word[..^1] + "ch";
            }

            string last = word[^1..];
            if (ReverseMap.TryGetValue(last, out var original))
                return word[..^1] + original;

            return word;
        }
    }
}
