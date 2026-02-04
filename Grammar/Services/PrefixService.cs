using System.Text.Json;

namespace Grammar.Services
{
    public class PrefixService
    {
        private readonly List<string> negationPrefixes;
        private readonly List<string> perfectivePrefixes;

        public PrefixService(string pathToSourceFilesFolder)
        {
            var json = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "prefixes.json"));
            var prefixesDict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(json)!;
            var containsPrefixes = prefixesDict.ContainsKey("perfective") && prefixesDict.ContainsKey("negation");
            if (!containsPrefixes)
            {
                throw new Exception("No accurate prefixes found!");
            }

            perfectivePrefixes = prefixesDict["perfective"];
            negationPrefixes = prefixesDict["negation"];
        }

        public string FindPerfectivePrefix(string lemma)
        {
            return perfectivePrefixes
            .OrderByDescending(p => p.Length)
            .FirstOrDefault(p => lemma.StartsWith(p) && lemma.Length > p.Length + 1)!;
        }

        public string GetNegativePrefix()
        {
            return negationPrefixes[0];
        }

        public bool HasPerfectivePrefix(string lemma)
        {
            return FindPerfectivePrefix(lemma) != null;
        }
    }
}