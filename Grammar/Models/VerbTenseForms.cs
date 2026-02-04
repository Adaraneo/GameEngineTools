namespace Grammar.Models
{
    public class VerbTenseForms
    {
        public Dictionary<string, string>? Plural { get; set; }

        // Present/Future: number → person
        public Dictionary<string, string>? Singular { get; set; }
    }
}