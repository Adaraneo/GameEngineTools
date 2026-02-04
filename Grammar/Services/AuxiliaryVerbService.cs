using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grammar.Logic;
using Grammar.Models;

namespace Grammar.Services
{
    public class AuxiliaryVerbService
    {
        private readonly DeclensionEngine engine;

        public AuxiliaryVerbService(DeclensionEngine engine)
        {
            this.engine = engine;
        }

        public string GetBeForm(Tense tense, GrammaticalNumber number, int person, Modus? modus, Gender gender, bool isNegative = false)
        {
            if (tense == Tense.Present && number == GrammaticalNumber.Singular && person == 3)
                return isNegative ? "není" : "je";

            var request = new WordRequest
            {
                Lemma = "být",
                Pattern = "být",
                Category = WordCategory.Verb,
                Tense = tense,
                Number = number,
                Person = person,
                Gender = gender,
                Modus = modus
            };

            var baseForm = engine.GetForm(request);

            return isNegative ? $"ne{baseForm}" : baseForm;
        }
    }
}
