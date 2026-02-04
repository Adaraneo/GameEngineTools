namespace Grammar.Models
{
    public class ConditionalParticles
    {
        public Dictionary<string, string> Plural { get; set; }
        public Dictionary<string, string> Singular { get; set; }
    }

    public class NegationParticle
    {
        public string Prefix { get; set; }
    }

    public class ParticlesData
    {
        public ConditionalParticles Conditional { get; set; }
        public NegationParticle Negation { get; set; }
        public ReflexiveParticles Reflexive { get; set; }
    }

    public class ReflexiveParticles
    {
        public string Dative { get; set; }
        public string Standart { get; set; }
    }
}