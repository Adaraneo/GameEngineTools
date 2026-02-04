using System.Text.Json;
using Grammar.Helpers;
using Grammar.Models;

namespace Grammar.Services
{
    public class NounDeclensionService
    {
        private readonly Dictionary<string, NounPattern> nounPatterns;
        private readonly Dictionary<string, NounPattern> irregularNouns;
        private readonly Dictionary<string, NounPattern> properNouns;

        public NounDeclensionService(string pathToSourceFilesFolder)
        {
            var nounJson = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "substantive_patterns.json"));
            nounPatterns = JsonSerializer.Deserialize<Dictionary<string, NounPattern>>(nounJson, Program.SerializerOptions)!;

            nounJson = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "substantive_irregular.json"));
            irregularNouns = JsonSerializer.Deserialize<Dictionary<string, NounPattern>>(nounJson, Program.SerializerOptions)!;

            nounJson = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "substantive_proper.json"));
            properNouns = JsonSerializer.Deserialize<Dictionary<string, NounPattern>>(nounJson, Program.SerializerOptions)!;
        }

        public string GetForm(WordRequest request)
        {
            if (properNouns.TryGetValue(request.Lemma, out var propers) && propers.IsIndeclinable)
            {
                return request.Lemma;
            }

            if (!nounPatterns.TryGetValue(request.Pattern.ToLower(), out var pattern))
            {
                throw new NotSupportedException($"Noun pattern '{request.Pattern}' not found.");
            }

            if (pattern.IsPluralOnly && request.Number == GrammaticalNumber.Singular)
            {
                throw new InvalidOperationException($"{request.Lemma} se nevyskytuje v jednotném čísle.");
            }


            var isLemmaPattern = request.Lemma == request.Pattern;

            var numberKey = request.Number == GrammaticalNumber.Singular ? "singular" : "plural";
            var caseKey = ((int)request.Case).ToString();

            if (irregularNouns.TryGetValue(request.Lemma.ToLower(), out var irregular))
            {
                if (irregular.Overrides != null &&
                    irregular.Overrides.TryGetValue(numberKey, out var cases) &&
                    cases.TryGetValue(caseKey, out var irregularForm))
                {
                    return irregularForm;
                }

                if (!string.IsNullOrEmpty(irregular.InheritsFrom))
                {
                    request.Pattern = irregular.InheritsFrom;
                }
            }

            if (!pattern.Endings.TryGetValue(numberKey, out var caseDict) ||
                !caseDict.TryGetValue(caseKey, out var ending))
                throw new InvalidOperationException($"Ending not found for {numberKey} {caseKey}.");

            // případná výjimka před výpočtem tvaru
            if (pattern.Overrides != null &&
                pattern.Overrides.TryGetValue(numberKey, out var caseOverrides) &&
                caseOverrides.TryGetValue(caseKey, out var overrideForm) &&
                isLemmaPattern)
            {
                return overrideForm;
            }

            var (prefix, stem) = MorphologyHelper.GetPrefixAndStemForNounAndAdjective(request);
            if (!string.IsNullOrEmpty(pattern.Stem) && isLemmaPattern)
            {
                stem = pattern.Stem!;
            }

            return MorphologyHelper.ApplyFormEnding(stem, ending);
        }

        public (Gender, string, GrammaticalNumber) GuessGenderAndPattern(string lemma)
        {
            throw new NotImplementedException();
            var lower = lemma.ToLowerInvariant();

            if (lower.EndsWith("a"))
                return (Gender.Feminine, "žena", GrammaticalNumber.Singular);

            if (lower.EndsWith("o"))
                return (Gender.Neuter, "město", GrammaticalNumber.Singular);

            if (lower.EndsWith("í"))
                return (Gender.Neuter, "stavení", GrammaticalNumber.Singular);

            if (lower.EndsWith("e") || lower.EndsWith("ě"))
                return (Gender.Neuter, "moře", GrammaticalNumber.Singular);

            if (lower.EndsWith("us") || lower.EndsWith("ec") || lower.EndsWith("tel"))
                return (Gender.MasculineAnimate, "muž", GrammaticalNumber.Singular);

            if (lower.EndsWith("y") || lower.EndsWith("i") || lower.EndsWith("é"))
                return (Gender.Feminine, "žena", GrammaticalNumber.Plural); // fallback plurál

            // fallback pro souhláskové zakončení
            if ("bcčdďfghjklmnprstvwxyzž".Contains(lower[^1]))
                return (Gender.MasculineInanimate, "hrad", GrammaticalNumber.Singular);

            // default fallback
            return (Gender.MasculineAnimate, "muž", GrammaticalNumber.Singular);
        }
    }
}
