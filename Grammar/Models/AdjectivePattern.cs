namespace Grammar.Models
{
    public class AdjectivePattern
    {
        public Dictionary<string, Dictionary<string, List<string>>> Endings { get; set; }
        /// <summary>
        /// Hard, soft or possessive
        /// </summary>
        public string Type { get; set; }
    }
}