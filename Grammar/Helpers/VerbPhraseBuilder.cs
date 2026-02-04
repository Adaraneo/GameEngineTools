using Grammar.Logic;
using Grammar.Models;
using Grammar.Services;

namespace Grammar.Helpers
{
    public class VerbPhraseBuilder
    {
        private readonly AuxiliaryVerbService auxVerbService;
        private readonly ParticleService particleService;
        private readonly PrefixService prefixService;

        private string BuildConditionalAuxiliary(string verbForm, GrammaticalNumber number, int person, bool explicitSubject, bool isNegative)
        {
            var particle = particleService.GetConditionalParticle(number, person);
            var negation = isNegative ? prefixService.GetNegativePrefix() : string.Empty;
            return explicitSubject ? $"{particle} {negation}{verbForm}" : $"{negation}{verbForm} {particle}";
        }

        public VerbPhraseBuilder(DeclensionEngine engine, ParticleService particleService, PrefixService prefixService)
        {
            auxVerbService = new AuxiliaryVerbService(engine);
            this.particleService = particleService;
            this.prefixService = prefixService;
        }

        public string BuildConditionalPhrase(string verbForm, GrammaticalNumber number, int person, bool explicitSubject, bool isNegative)
        {
            return BuildConditionalAuxiliary(verbForm, number, person, explicitSubject, isNegative);
        }

        public string BuildPassiveConditionalPhrase(string verbForm, GrammaticalNumber number, int person, Modus? modus, Gender gender, bool isNegative)
        {
            var beForm = auxVerbService.GetBeForm(Tense.Past, number, person, modus, gender, isNegative);
            verbForm = BuildConditionalAuxiliary(verbForm, number, person, true, false);
            return $"{beForm} {verbForm}";
        }

        public string BuildPassivePhrase(string verbForm, Tense tense, GrammaticalNumber number, int person, Modus? modus, Gender gender, bool isNegative)
        {
            var beForm = auxVerbService.GetBeForm(tense, number, person, modus, gender, isNegative);
            return $"{beForm} {verbForm}";
        }

        public string BuildReflexivePhrase(string verbForm, bool isDative)
        {
            var reflexive = particleService.GetReflexive(isDative);
            return $"{verbForm} {reflexive}";
        }

        public string BuildSynteticFuturePhrase(string verbForm, GrammaticalNumber number, int person, Modus? modus, Gender gender, bool isNegative)
        {
            var beForm = auxVerbService.GetBeForm(Tense.Future, number, person, modus, gender, isNegative);
            return $"{beForm} {verbForm}";
        }
    }
}