using System.Text.Json;
using Grammar.Helpers;
using Grammar.Models;

namespace Grammar.Services
{
    public class AdjectiveDeclensionService
    {
        private readonly Dictionary<string, AdjectivePattern> adjectivePatterns;

        public AdjectiveDeclensionService(string pathToSourceFilesFolder)
        {
            var adjJson = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "adjective_patterns.json"));
            adjectivePatterns = JsonSerializer.Deserialize<Dictionary<string, AdjectivePattern>>(adjJson, Program.SerializerOptions)!;
        }

        public string GetForm(WordRequest request)
        {
            if (!adjectivePatterns.TryGetValue(request.Pattern.ToLower(), out var pattern))
            {
                throw new NotSupportedException($"Adjective pattern '{request.Pattern}' not found.");
            }

            var numberKey = request.Number == GrammaticalNumber.Singular ? "singular" : "plural";
            var genderKey = request.Gender.ToString();
            var caseIndex = (int)request.Case - 1;

            if (!pattern.Endings.TryGetValue(numberKey, out var genderDict) ||
                !genderDict.TryGetValue(genderKey, out var endings))
            {
                throw new InvalidOperationException($"Ending not found for {numberKey} {genderKey}.");
            }

            if (caseIndex < 0 || caseIndex >= endings.Count)
            {
                throw new IndexOutOfRangeException("Invalid case index for adjective.");
            }

            var (prefix, stem) = MorphologyHelper.GetPrefixAndStemForNounAndAdjective(request);
            return MorphologyHelper.ApplyFormEnding(stem, endings[caseIndex]);
        }

        public string GuessAdjectivePattern(string lemma)
        {
            if (lemma.EndsWith("ý") || lemma.EndsWith("á") || lemma.EndsWith("é") || lemma.EndsWith("í"))
            {
                return lemma.EndsWith("í") ? "jarní" : "mladý";
            }

            return "mladý"; // fallback na tvrdý vzor
        }
    }
}