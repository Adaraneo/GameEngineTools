using System.Text.Json;
using Grammar.Models;
using Grammar.Services;

namespace Grammar.Logic
{
    public class DeclensionEngine
    {
        private readonly Dictionary<string, PrepositionPattern> prepositionPatterns;
        private readonly AdjectiveDeclensionService adjectiveDeclension;
        private readonly NounDeclensionService nounDeclension;
        private readonly VerbConjugationService verbDeclension;

        public DeclensionEngine(string pathToSourceFilesFolder)
        {
            var prepositionsJson = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "prepositions.json"));
            prepositionPatterns = JsonSerializer.Deserialize<Dictionary<string, PrepositionPattern>>(prepositionsJson, Program.SerializerOptions)!;
            nounDeclension = new NounDeclensionService(pathToSourceFilesFolder);
            adjectiveDeclension = new AdjectiveDeclensionService(pathToSourceFilesFolder);
            verbDeclension = new VerbConjugationService(pathToSourceFilesFolder);
        }

        public string GetForm(WordRequest request)
        {
            return request.Category switch
            {
                WordCategory.Substantive or WordCategory.ProperNoun => nounDeclension.GetForm(request),
                WordCategory.Adjective => adjectiveDeclension.GetForm(request),
                WordCategory.Verb => verbDeclension.GetForm(request),
                _ => throw new NotSupportedException($"Unsupported category: {request.Category}")
            };
        }
    }
}