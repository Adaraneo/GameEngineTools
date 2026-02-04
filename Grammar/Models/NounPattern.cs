namespace Grammar.Models
{
    public class NounPattern
    {
        public Dictionary<string, Dictionary<string, string>> Endings { get; set; }
        public string Gender { get; set; }

        public Dictionary<string, Dictionary<string, string>>? Overrides { get; set; }
        public string? Stem { get; set; }
        public string? InheritsFrom { get; set; }
        public bool IsIndeclinable { get; set; } = false;
        public bool IsPluralOnly { get; set; } = false;
    }
}