using Grammar.Models;
using Grammar.Services;

namespace Grammar.Helpers
{
    public static class MorphologyHelper
    {
        public static string ApplyFormEnding(string stem, string ending)
        {
            if (IsEnding(ending))
            {
                return stem + ending.Replace("-", "");
            }
            else
            {
                return ending;
            }
        }

        public static bool EndsWithTwoConsonants(string stem)
        {
            if (stem.Length < 2)
            {
                return false;
            }

            var last = stem[^1];
            var secondLast = stem[^2];
            return IsConsonant(secondLast) && IsConsonant(last);
        }

        public static bool EndsWithVowelConsonantVowelConsonant(string lemma)
        {
            if (lemma.Length < 4)
            {
                return false;
            }

            var vowel = lemma[^4];
            var consonant = lemma[^3];
            var lastVowel = lemma[^2];
            var lastConsonant = lemma[^1];
            return !IsConsonant(vowel) && IsConsonant(consonant) && !IsConsonant(lastVowel) && IsConsonant(lastConsonant);
        }

        /// <summary>
        /// TODO: Add PrefixService
        /// </summary>
        public static (string?, string) GetPrefixAndStemForNounAndAdjective(WordRequest word)
        {
            var lemma = word.Lemma;
            var patternName = word.Pattern;
            string prefix = null;
            if (lemma.EndsWith(patternName[^1]))
            {
                return (prefix, lemma[..^1]);
            }

            if (EndsWithVowelConsonantVowelConsonant(lemma) && word.Case != GrammaticalCase.Nominative)
            {
                var lemmaTemp = lemma[..^2];
                return (prefix, lemmaTemp + lemma[^1]);
            }

            return (prefix, lemma);
        }

        public static (string?, string) GetPrefixAndStemForVerb(WordRequest word, Dictionary<string, VerbPattern> verbPatterns, Dictionary<string, VerbPattern> irregularVerbPatterns, PrefixService prefixService)
        {
            var lemma = word.Lemma;
            var patternName = word.Pattern;
            string prefix = prefixService.FindPerfectivePrefix(lemma);

            if (prefix != null)
            {
                lemma = lemma.Substring(prefix.Length);
            }

            if (verbPatterns.TryGetValue(patternName, out var pattern))
            {
                var stem = GetStemForTense(pattern, word);
                return (prefix, stem);
            }
            else if (irregularVerbPatterns.TryGetValue(patternName, out pattern))
            {
                var stem = GetStemForTense(pattern, word);
                return (prefix, stem);
            }

            // Fallback heuristiky
            if (patternName.StartsWith("trida"))
            {
                if (lemma.EndsWith("ovat")) return (prefix, lemma[..^4]);             // pracovat → prac
                if (lemma.EndsWith("nout")) return (prefix, lemma[..^4]);             // klesnout → kles
                if (lemma.EndsWith("ít")) return (prefix, lemma[..^2]);               // sázet → sáz
                if (lemma.EndsWith("ět") || lemma.EndsWith("et")) return (prefix, lemma[..^2]); // myslet → mysl
                if (lemma.EndsWith("it")) return (prefix, lemma[..^2]);               // prosit → pros
                if (lemma.EndsWith("át") || lemma.EndsWith("at")) return (prefix, lemma[..^2]); // zpívat → zpív

                return (prefix, lemma.Substring(0, lemma.Length - 1)); // fallback
            }

            if (lemma == patternName && word.Category == WordCategory.Verb)
                return (prefix, lemma);

            if (lemma.EndsWith(patternName[^1]))
                return (prefix, lemma[..^1]);

            return (prefix, lemma); // ultimate fallback
        }

        public static string? GetPrefixForVerb(WordRequest word, PrefixService prefixService)
        {
            var lemma = word.Lemma;
            return prefixService.FindPerfectivePrefix(lemma);
        }

        /// <summary>
        /// Vrací správný kmen podle času, osoby a případně rodu na základě WordRequest a VerbPattern.
        /// </summary>
        public static string GetStemForTense(VerbPattern pattern, WordRequest request)
        {
            return request.Tense switch
            {
                Tense.Present => pattern.PresentStem ?? pattern.Stem ?? throw new InvalidOperationException("Missing Present stem."),
                Tense.Past => pattern.PastStem ?? pattern.Stem ?? throw new InvalidOperationException("Missing Past stem."),
                Tense.Future when pattern.Aspect == VerbAspect.Perfective => pattern.FutureStem ?? pattern.PresentStem ?? pattern.Stem ?? throw new InvalidOperationException("Missing Future stem."),
                Tense.Future when pattern.Aspect == VerbAspect.Imperfective && request.Lemma == "být" => pattern.FutureStem ?? throw new InvalidOperationException("Missing Future stem for 'být'"),
                Tense.Future when pattern.Aspect == VerbAspect.Imperfective => pattern.Infinitive ?? request.Lemma ?? throw new InvalidOperationException("Missing infinitive!"),
                _ => pattern.PassiveStem ?? pattern.Stem ?? throw new InvalidOperationException("Missing base or passive stem.")
            };
        }

        public static bool IsConsonant(char c)
        {
            return !"aeiouáéíóúýě".Contains(char.ToLower(c)); // české samohlásky
        }

        public static bool IsEnding(string ending) => ending.Contains("-");
    }
}