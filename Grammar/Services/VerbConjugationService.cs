using System.Text.Json;
using Grammar.Helpers;
using Grammar.Models;

namespace Grammar.Services
{
    public class VerbConjugationService
    {
        private readonly Dictionary<string, VerbPattern> irregularVerbPatterns;
        private readonly PrefixService prefixService;
        private readonly Dictionary<VerbClass, string> verbClassMap = new Dictionary<VerbClass, string>()
        {
            { VerbClass.Class1, "trida1" },
            { VerbClass.Class2, "trida2" },
            { VerbClass.Class3, "trida3" },
            { VerbClass.Class4, "trida4" },
            { VerbClass.Class5, "trida5" },
        };

        private readonly Dictionary<string, VerbPattern> verbPatterns;
        private VerbPattern ClonePattern(VerbPattern src)
        {
            return new VerbPattern
            {
                Aspect = src.Aspect,
                Stem = src.Stem,

                Present = src.Present != null ? new VerbTenseForms
                {
                    Singular = src.Present.Singular != null ? new Dictionary<string, string>(src.Present.Singular) : null,
                    Plural = src.Present.Plural != null ? new Dictionary<string, string>(src.Present.Plural) : null
                } : null,

                Future = src.Future != null ? new VerbTenseForms
                {
                    Singular = src.Future.Singular != null ? new Dictionary<string, string>(src.Future.Singular) : null,
                    Plural = src.Future.Plural != null ? new Dictionary<string, string>(src.Future.Plural) : null
                } : null,

                PastParticiple = src.PastParticiple?.ToDictionary(
                    g => g.Key,
                    g => g.Value.ToDictionary(n => n.Key, n => n.Value)),

                PassiveParticiple = src.PassiveParticiple?.ToDictionary(
                    g => g.Key,
                    g => g.Value.ToDictionary(n => n.Key, n => n.Value))
            };
        }

        private string GetImperativeForm(WordRequest word)
        {
            var lemma = word.Lemma;
            var (prefix, stem) = MorphologyHelper.GetPrefixAndStemForVerb(word, verbPatterns, irregularVerbPatterns, prefixService);
            if (irregularVerbPatterns.TryGetValue(word.Pattern.ToLower(), out var irregularPattern) && !string.IsNullOrEmpty(irregularPattern.ImperativeStem))
            {
                var baseImperative = irregularPattern.ImperativeStem;

                string result = word.Number switch
                {
                    GrammaticalNumber.Singular when word.Person == 2 => baseImperative,
                    GrammaticalNumber.Plural when word.Person == 1 => baseImperative + "me",
                    GrammaticalNumber.Plural when word.Person == 2 => baseImperative + "te",
                    _ => throw new InvalidOperationException("Imperative exists only for 2nd person (sg/pl) and 1st person plural.")
                };

                if (!string.IsNullOrEmpty(word.Reflexive))
                {
                    result += $" {word.Reflexive}";
                }

                return $"{prefix}{result}";
            }

            if (irregularVerbPatterns.TryGetValue(word.Pattern.ToLower(), out var irrefgularPattern))
            {
                var baseStem = irregularPattern.ImperativeStem ?? irrefgularPattern.Stem ?? word.Lemma;

                string baseImperative = baseStem;
                if (word.Number == GrammaticalNumber.Singular && word.Person == 2)
                {
                    if (MorphologyHelper.EndsWithTwoConsonants(baseStem))
                    {
                        baseImperative += "i";
                    }
                }
                else if (word.Number == GrammaticalNumber.Plural && word.Person == 1)
                {
                    baseImperative += "me";
                }
                else if (word.Number == GrammaticalNumber.Plural && word.Person == 2)
                {
                    baseImperative += "te";
                }
                else
                {
                    throw new InvalidOperationException("Imperative exists only for 2nd person (sg/pl) and 1st person plural.");
                }

                if (!string.IsNullOrEmpty(word.Reflexive))
                {
                    baseImperative += $" {word.Reflexive}";
                }

                return $"{prefix}{baseImperative}";
            }

            // Heuristická pravidla:
            var imperativeForm = (word.Number, word.Person) switch
            {
                (GrammaticalNumber.Singular, 2) => $"{prefix}{stem}",
                (GrammaticalNumber.Plural, 1) => $"{prefix}{stem}me",
                (GrammaticalNumber.Plural, 2) => $"{prefix}{stem}te",
                _ => throw new InvalidOperationException("Imperative exists only for 2nd person (sg/pl) and 1st person plural.")
            };

            if (!string.IsNullOrEmpty(word.Reflexive))
            {
                imperativeForm += $" {word.Reflexive}";
            }

            return $"{prefix}{imperativeForm}!";
        }

        private string GetPassiveForm(WordRequest word, VerbPattern pattern, string genderKey, string numberKey, string stem)
        {
            var lemma = word.Lemma;
            var prefix = MorphologyHelper.GetPrefixForVerb(word, prefixService);

            string ending = string.Empty;
            if (pattern.PassiveParticiple.TryGetValue(genderKey, out var participleDict) && participleDict.TryGetValue(numberKey, out var participle))
            {
                ending = participle;
            }

            // Heuristická úprava kmene:
            if (stem.EndsWith("sk"))
            {
                stem = stem[..^2] + "ště"; // tisk → tištěn
            }
            else if (stem.EndsWith("s"))
            {
                stem = stem[..^1] + "š"; // pros → proš
            }
            else if (lemma == "kvést")
            {
                stem = "květ"; // nepravidelné
            }

            return prefix + MorphologyHelper.ApplyFormEnding(stem, ending);
        }

        public VerbConjugationService(string pathToSourceFilesFolder)
        {
            var json = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "verb_patterns.json"));
            verbPatterns = JsonSerializer.Deserialize<Dictionary<string, VerbPattern>>(json, Program.SerializerOptions)!;

            json = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "verb_irregular.json"));
            irregularVerbPatterns = JsonSerializer.Deserialize<Dictionary<string, VerbPattern>>(json, Program.SerializerOptions)!;

            prefixService = new PrefixService(pathToSourceFilesFolder);
        }

        public string GetForm(WordRequest request)
        {
            if (request.Modus == Modus.Imperative && request.IsPassive)
            {
                throw new InvalidOperationException("Passive form does not exist in imperative.");
            }

            if (request.IsPassive && request.Lemma.Equals("být", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Impossible to create passive for verb 'být'.");
            }

            if (request.VerbClass.HasValue && !verbPatterns.ContainsKey(request.Pattern.ToLower()))
            {
                if (!verbClassMap.TryGetValue(request.VerbClass.Value, out var mappedPattern))
                    throw new InvalidOperationException($"Unknown verb class {request.VerbClass.Value}");

                request.Pattern = mappedPattern;
            }

            if (!verbPatterns.TryGetValue(request.Pattern.ToLower(), out var pattern))
            {
                if (irregularVerbPatterns.TryGetValue(request.Pattern.ToLower(), out var irregularPattern))
                {
                    if (!string.IsNullOrEmpty(irregularPattern.InheritsFrom) && verbPatterns.TryGetValue(irregularPattern.InheritsFrom.ToLower(), out var inheritedPattern))
                    {
                        pattern = ClonePattern(inheritedPattern);
                        //if (verbClassMap.ContainsValue(irregularPattern.InheritsFrom))
                        //{
                        //    request.VerbClass = verbClassMap.First(k => (k.Value == irregularPattern.InheritsFrom)).Key;
                        //}

                        if (!string.IsNullOrEmpty(irregularPattern.Stem)) pattern.Stem = irregularPattern.Stem;
                        if (!string.IsNullOrEmpty(irregularPattern.FutureStem)) pattern.FutureStem = irregularPattern.FutureStem;
                        if (!string.IsNullOrEmpty(irregularPattern.PresentStem)) pattern.PresentStem = irregularPattern.PresentStem;
                        if (!string.IsNullOrEmpty(irregularPattern.PastStem)) pattern.PastStem = irregularPattern.PastStem;
                        if (!string.IsNullOrEmpty(irregularPattern.PassiveStem)) pattern.PassiveStem = irregularPattern.PassiveStem;
                        if (irregularPattern.Aspect != null) pattern.Aspect = irregularPattern.Aspect;
                        if (irregularPattern.Present != null) pattern.Present = irregularPattern.Present;
                        if (irregularPattern.Future != null) pattern.Future = irregularPattern.Future;
                        if (irregularPattern.PastParticiple != null) pattern.PastParticiple = irregularPattern.PastParticiple;
                        if (irregularPattern.PassiveParticiple != null) pattern.PassiveParticiple = irregularPattern.PassiveParticiple;
                    }
                    else
                    {
                        pattern = irregularPattern;
                    }
                }
            }

            var numberKey = request.Number == GrammaticalNumber.Singular ? "singular" : "plural";
            var personKey = request.Person.ToString();

            if (request.Tense == Tense.Present && pattern.Aspect == VerbAspect.Perfective)
            {
                request.Tense = Tense.Future;
            }

            var tenseKey = request.Tense.ToString().ToLower();

            string? GetGenderKey(WordRequest request, out string? genderKey)
            {
                genderKey = request.Gender switch
                {
                    Gender.MasculineAnimate or Gender.MasculineInanimate => "masculine",
                    Gender.Feminine => "feminine",
                    Gender.Neuter => "neuter",
                    _ => throw new NotSupportedException("Unsupported gender.")
                };

                return genderKey;
            }

            if (request.IsPassive)
            {
                GetGenderKey(request, out var genderKey);

                return GetPassiveForm(request, pattern!, genderKey!, numberKey, pattern.PassiveStem ?? pattern.Stem);
            }

            if (request.Modus == Modus.Conditional)
            {
                GetGenderKey(request, out var genderKey);
                var participle = pattern.PastParticiple[genderKey][numberKey];

                var (prefix, stem) = (MorphologyHelper.GetPrefixForVerb(request, prefixService), pattern.PastStem ?? pattern.Stem);

                if (!string.IsNullOrEmpty(prefix))
                {
                    stem = prefix + stem;
                }

                return MorphologyHelper.ApplyFormEnding(stem, participle);
            }

            if (request.Modus == Modus.Imperative)
            {
                return GetImperativeForm(request);
            }

            if (request.Tense == Tense.Past)
            {
                if (!pattern.PastParticiple.TryGetValue(GetGenderKey(request, out var genderKey), out var participleDict) ||
                    !participleDict.TryGetValue(numberKey, out var participle))
                {
                    throw new InvalidOperationException($"Past participle not found for {genderKey} {numberKey}.");
                }

                var (prefix, stem) = MorphologyHelper.GetPrefixAndStemForVerb(request, verbPatterns, irregularVerbPatterns, prefixService);

                if (!string.IsNullOrEmpty(prefix))
                {
                    stem = prefix + stem;
                }

                return MorphologyHelper.ApplyFormEnding(stem, participle);
            }
            else
            {
                var tenseForms = request.Tense switch
                {
                    Tense.Present => pattern.Present,
                    Tense.Future => pattern.Future ?? pattern.Present,
                    _ => throw new InvalidOperationException("Unsuported tense.")
                };

                // Person dictionary
                Dictionary<string, string>? pDict = numberKey switch
                {
                    "singular" => tenseForms.Singular,
                    "plural" => tenseForms.Plural,
                    _ => null
                };

                if (pDict == null || !pDict.TryGetValue(personKey, out var ending))
                    throw new InvalidOperationException($"Ending not found for {tenseKey} {numberKey} person {personKey}");

                var (prefix, stem) = MorphologyHelper.GetPrefixAndStemForVerb(request, verbPatterns, irregularVerbPatterns, prefixService);

                if (!string.IsNullOrEmpty(prefix))
                {
                    stem = prefix + stem;
                }

                if (request.Tense == Tense.Future && request.Lemma != "být")
                {
                    if (request.Aspect == VerbAspect.Imperfective)
                    {
                        return MorphologyHelper.ApplyFormEnding(stem, ending);
                    }
                    else
                    {
                        return pattern.Infinitive ?? request.Lemma;
                    }
                }
                else
                {
                    return MorphologyHelper.ApplyFormEnding(stem, ending);
                }
            }

            throw new InvalidOperationException("Form generation failed");
        }

        public VerbAspect GuessVerbAspect(string lemma)
        {
            return prefixService.HasPerfectivePrefix(lemma)
                ? VerbAspect.Perfective
                : VerbAspect.Imperfective;
        }

        public VerbClass? GuessVerbClass(string lemma)
        {
            if (verbPatterns.ContainsKey(lemma.ToLower()))
            {
                return null;
            }

            if (lemma.EndsWith("ovat"))
                return VerbClass.Class3;

            if (lemma.EndsWith("it") || lemma.EndsWith("et") || lemma.EndsWith("ět"))
                return VerbClass.Class2;

            if (lemma.EndsWith("at") || lemma.EndsWith("át"))
                return VerbClass.Class1;

            // volitelně další:
            if (lemma.EndsWith("nout"))
                return VerbClass.Class4;

            if (lemma.EndsWith("ít"))
                return VerbClass.Class5;

            return null;
        }

        public string GuessVerbPattern(string lemma)
        {
            // Můžeš to doplnit vlastním seznamem známých vzorů
            return lemma.ToLower(); // fallback: pattern = infinitiv
        }
    }
}