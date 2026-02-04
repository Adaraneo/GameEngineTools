using System.Text.Json;
using System.Text.Json.Serialization;
using Grammar.Helpers;
using Grammar.Logic;
using Grammar.Models;
using Grammar.Models.JsonConverters;
using Grammar.Services;

namespace Grammar
{
    internal class Program
    {
        private static void FormsTest(DeclensionEngine engine, string filename)
        {
            var jsonInput = File.ReadAllText($"Data/{filename}.json");
            var requests = JsonSerializer.Deserialize<List<WordRequest>>(jsonInput, SerializerOptions);

            foreach (var req in requests)
            {
                Console.WriteLine($"\n{req.Lemma} ({req.Pattern}) [{req.Category}]:");

                if (req.Category == WordCategory.Verb)
                {
                    foreach (bool isPassive in new[] { false, true })
                        foreach (var gender in Enum.GetValues<Gender>())
                        {
                            foreach (Tense tense in Enum.GetValues(typeof(Tense)))
                            {
                                Console.WriteLine($"  {(isPassive ? "Passive" : "Active")} {tense} tense ({gender}):");
                                foreach (GrammaticalNumber number in Enum.GetValues(typeof(GrammaticalNumber)))
                                {
                                    for (int person = 1; person <= 3; person++)
                                    {
                                        var formRequest = new WordRequest
                                        {
                                            Lemma = req.Lemma,
                                            Pattern = req.Pattern,
                                            Category = req.Category,
                                            Tense = tense,
                                            Number = number,
                                            Person = person,
                                            Gender = gender,
                                            Aspect = req.Aspect,
                                            IsPassive = isPassive,
                                        };

                                        try
                                        {
                                            var form = engine.GetForm(formRequest);
                                            Console.WriteLine($"    {person}. {number}: {form}");
                                        }
                                        catch (Exception ex)
                                        {
                                            Console.WriteLine($"    {person}. {number}: ERROR – {ex.Message}");
                                        }
                                    }
                                }
                            }
                        }
                }
                else
                {
                    foreach (GrammaticalNumber number in Enum.GetValues(typeof(GrammaticalNumber)))
                    {
                        Console.WriteLine($"  {number}:");
                        for (int i = 1; i <= 7; i++)
                        {
                            var formRequest = new WordRequest
                            {
                                Lemma = req.Lemma,
                                Pattern = req.Pattern,
                                Category = req.Category,
                                Gender = req.Gender,
                                Number = number,
                                Case = (GrammaticalCase)i
                            };

                            try
                            {
                                var form = engine.GetForm(formRequest);
                                Console.WriteLine($"    {(GrammaticalCase)i}: {form}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"    {(GrammaticalCase)i}: ERROR – {ex.Message}");
                            }
                        }
                    }
                }
            }
        }

        private static void VerbPhraseTest(DeclensionEngine engine)
        {
            var particleService = new ParticleService("Data");
            var prefixService = new PrefixService("Data");
            var verbPhraseBuilder = new VerbPhraseBuilder(engine, particleService, prefixService);
            var negationService = new NegationService(engine, prefixService);
            var words = new WordRequest[]
            {
                new WordRequest {
                    Lemma = "psát",
                    Category = WordCategory.Verb,
                    Person = 1,
                    Number = GrammaticalNumber.Singular,
                    Tense = Tense.Present,
                    Gender = Gender.Feminine,
                    Pattern = "psát",
                    Modus = Modus.Indicative
                },
                new WordRequest
                {
                    Lemma = "nést",
                    Category = WordCategory.Verb,
                    Person = 3,
                    Number = GrammaticalNumber.Singular,
                    Tense = Tense.Present,
                    Gender = Gender.Feminine,
                    Pattern = "nese",
                    Modus = Modus.Indicative
                }
            };

            foreach (var word in words)
            {
                foreach (var passive in new[] { false, true })
                {
                    word.IsPassive = passive;
                    var originalTense = word.Tense;
                    var originalModus = word.Modus;
                    var form = passive ? verbPhraseBuilder.BuildPassivePhrase(engine.GetForm(word), word.Tense, word.Number, word.Person, word.Modus, word.Gender, false) : engine.GetForm(word);
                    var negativeForm = passive ? verbPhraseBuilder.BuildPassivePhrase(engine.GetForm(word), word.Tense, word.Number, word.Person, word.Modus, word.Gender, true) : negationService.ApplyNegation(word, engine.GetForm(word));
                    Console.WriteLine("Form: {0}", form);
                    Console.WriteLine("Negative form: {0}", negativeForm);

                    word.Modus = Modus.Conditional;
                    form = passive ? verbPhraseBuilder.BuildPassiveConditionalPhrase(engine.GetForm(word), word.Number, word.Person, word.Modus, word.Gender, false) : verbPhraseBuilder.BuildConditionalPhrase(engine.GetForm(word), word.Number, word.Person, false, false);
                    negativeForm = passive ? verbPhraseBuilder.BuildPassiveConditionalPhrase(engine.GetForm(word), word.Number, word.Person, word.Modus, word.Gender, true) : verbPhraseBuilder.BuildConditionalPhrase(engine.GetForm(word), word.Number, word.Person, false, true);
                    Console.WriteLine("Conditional form: {0}", form);
                    Console.WriteLine("Conditional negative form: {0}", negativeForm);

                    if (!passive)
                    {
                        word.Modus = Modus.Imperative;
                        var originalPerson = word.Person;
                        word.Person = 2;
                        form = engine.GetForm(word);
                        negativeForm = negationService.ApplyNegation(word, form);
                        Console.WriteLine("Imperative form: {0}!", form);
                        Console.WriteLine("Imperative negative form: {0}!", negativeForm);
                        word.Person = originalPerson;
                    }

                    word.Modus = Modus.Indicative;
                    word.Tense = Tense.Future;
                    if (word.Aspect == VerbAspect.Perfective)
                    {
                        form = verbPhraseBuilder.BuildSynteticFuturePhrase(engine.GetForm(word), word.Number, word.Person, word.Modus, word.Gender, false);
                        negativeForm = verbPhraseBuilder.BuildSynteticFuturePhrase(engine.GetForm(word), word.Number, word.Person, word.Modus, word.Gender, true);
                    }
                    else
                    {
                        form = engine.GetForm(word);
                        negativeForm = negationService.ApplyNegation(word, form);
                    }

                    Console.WriteLine("Future form: {0}", form);
                    Console.WriteLine("Future negative form: {0}", negativeForm);

                    word.Tense = Tense.Past;
                    form = passive ? verbPhraseBuilder.BuildPassivePhrase(engine.GetForm(word), word.Tense, word.Number, word.Person, word.Modus, word.Gender, false) : engine.GetForm(word);
                    negativeForm = passive ? verbPhraseBuilder.BuildPassivePhrase(engine.GetForm(word), word.Tense, word.Number, word.Person, word.Modus, word.Gender, true) : negationService.ApplyNegation(word, engine.GetForm(word));
                    Console.WriteLine("Past form: {0}", form);
                    Console.WriteLine("Past negative form: {0}", negativeForm);
                    word.Tense = originalTense;
                    word.Modus = originalModus;
                }
            }
        }

        internal static JsonSerializerOptions SerializerOptions
        {
            get
            {
                return new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    Converters = {
                        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                        new VerbTenseFormsConverter(),
                    },
                };
            }
        }

        private static void NounDeclensionTest(DeclensionEngine engine)
        {
            var words = new WordRequest[]
            {
                new WordRequest
                {
                    Lemma = "Vojtěch",
                    Category = WordCategory.ProperNoun,
                    Number = GrammaticalNumber.Singular,
                    Gender = Gender.MasculineAnimate,
                    Pattern = "pán",
                },
                new WordRequest
                {
                    Lemma = "oko",
                    Category = WordCategory.Substantive,
                    Number = GrammaticalNumber.Singular,
                    Gender = Gender.Neuter,
                    Pattern = "město",
                },
                new WordRequest
                {
                    Lemma = "oko",
                    Category = WordCategory.Substantive,
                    Number = GrammaticalNumber.Plural,
                    Gender = Gender.Neuter,
                    Pattern = "město",
                },
                new WordRequest
                {
                    Lemma = "okno",
                    Category = WordCategory.Substantive,
                    Number = GrammaticalNumber.Singular,
                    Gender = Gender.Neuter,
                    Pattern = "město",
                },
                new WordRequest
                {
                    Lemma= "chlap",
                    Category = WordCategory.Substantive,
                    Number = GrammaticalNumber.Singular,
                    Gender = Gender.MasculineAnimate,
                    Pattern = "pán",
                },
                new WordRequest
                {
                    Lemma = "chlapec",
                    Category = WordCategory.Substantive,
                    Number = GrammaticalNumber.Singular,
                    Gender = Gender.MasculineAnimate,
                    Pattern = "muž",
                },
            };

            foreach (var word in words)
            {
                Console.WriteLine("Word: {0}", word.Lemma);
                foreach (var gCase in new[] { GrammaticalCase.Nominative, GrammaticalCase.Genitive, GrammaticalCase.Dative, GrammaticalCase.Accusative, GrammaticalCase.Vocative, GrammaticalCase.Locative, GrammaticalCase.Instrumental })
                {
                    word.Case = gCase;
                    var form = engine.GetForm(word);
                    Console.WriteLine("{0}: {1}", word.Case.ToString(), form);
                }
            }
        }

        private static void AdjectiveDeclensionTest(DeclensionEngine engine, params WordRequest[] substantives)
        {
            foreach (var substantive in substantives)
            {
                var words = new WordRequest[]
                {
                    new WordRequest
                    {
                        Lemma = "mladý",
                        Category = WordCategory.Adjective,
                        Number = substantive.Number,
                        Gender = substantive.Gender,
                        Pattern = "mladý",
                    },
                };

                foreach (var word in words)
                {
                    Console.WriteLine("Word: {0}", word.Lemma);
                    foreach (var gCase in new[] { GrammaticalCase.Nominative, GrammaticalCase.Genitive, GrammaticalCase.Dative, GrammaticalCase.Accusative, GrammaticalCase.Vocative, GrammaticalCase.Locative, GrammaticalCase.Instrumental })
                    {
                        word.Case = gCase;
                        var form = engine.GetForm(word);
                        substantive.Case = gCase;
                        Console.WriteLine("{0}: {1} {2}", word.Case.ToString(), form, engine.GetForm(substantive));
                    }
                }
            }
        }

        internal static void Main(string[] args)
        {
            var engine = new DeclensionEngine("Data");

            //AdjectiveDeclensionTest(engine, new WordRequest { Lemma = "chlapec", Category = WordCategory.Substantive, Number = GrammaticalNumber.Singular, Gender = Gender.MasculineAnimate, Pattern = "muž" }, new WordRequest { Lemma = "chlap", Category = WordCategory.Substantive, Number = GrammaticalNumber.Singular, Gender = Gender.MasculineAnimate, Pattern = "pán" });

            NounDeclensionTest(engine);
        }
    }
}