namespace Grammar.Models
{
    public enum Gender
    { MasculineAnimate, MasculineInanimate, Feminine, Neuter }

    public enum GrammaticalCase
    { Nominative = 1, Genitive, Dative, Accusative, Vocative, Locative, Instrumental }

    public enum GrammaticalNumber
    { Singular, Plural }

    public enum WordCategory
    { Substantive, Adjective, Verb, ProperNoun }

    #region Verb Only

    /// <summary>
    /// Slovesný způsob.
    /// </summary>
    public enum Modus
    { Conditional, Imperative, Coniuctive, Indicative }

    /// <summary>
    /// Slovesný čas.
    /// </summary>
    public enum Tense
    { Present, Past, Future }

    /// <summary>
    /// Slovesný vid.
    /// </summary>
    public enum VerbAspect
    { Perfective, Imperfective }

    /// <summary>
    /// Slovesná třída.
    /// </summary>
    public enum VerbClass
    { Class1, Class2, Class3, Class4, Class5 }

    #endregion Verb Only

    public class WordRequest
    {
        public GrammaticalCase Case { get; set; }
        public WordCategory Category { get; set; }
        public Gender Gender { get; set; }
        public bool IsNegative { get; set; } = false;
        public string Lemma { get; set; }
        public GrammaticalNumber Number { get; set; }
        public string Pattern { get; set; }

        #region Verbs Only

        public VerbAspect Aspect { get; set; }
        public bool IsPassive { get; set; }
        public Modus? Modus { get; set; }
        public int Person { get; set; }
        public string? Reflexive { get; set; }
        public Tense Tense { get; set; }
        public VerbClass? VerbClass { get; set; }

        #endregion Verbs Only
    }
}