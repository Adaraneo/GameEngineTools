namespace Grammar.Models
{
    public class VerbPattern
    {
        public VerbAspect Aspect { get; set; }
        public VerbTenseForms Future { get; set; }
        public string? FutureStem { get; set; }

        // syntetický future (dokonavá slovesa)
        public string? ImperativeStem { get; set; }

        public string? Infinitive { get; set; }
        public string? InheritsFrom { get; set; }
        public Dictionary<string, Dictionary<string, string>> PassiveParticiple { get; set; }

        // imperativ
        public string? PassiveStem { get; set; }

        // trpný
        public Dictionary<string, Dictionary<string, string>> PastParticiple { get; set; }

        public string? PastStem { get; set; }

        // minulý čas
        public VerbTenseForms Present { get; set; }

        public string? PresentStem { get; set; }
        public string? Stem { get; set; }                // výchozí fallback
                                                         // přítomný čas
    }
}