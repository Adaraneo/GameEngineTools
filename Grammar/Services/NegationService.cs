using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grammar.Logic;
using Grammar.Models;

namespace Grammar.Services
{
    public class NegationService
    {
        private readonly AuxiliaryVerbService auxiliaryVerbService;
        private readonly PrefixService prefixService;

        public NegationService(DeclensionEngine engine, PrefixService prefixService)
        {
            auxiliaryVerbService = new AuxiliaryVerbService(engine);
            this.prefixService = prefixService;
        }

        public string ApplyNegation(WordRequest request, string baseForm)
        {
            if (request.Lemma == "být")
            {
                return auxiliaryVerbService.GetBeForm(request.Tense, request.Number, request.Person, request.Modus, request.Gender, isNegative: true);
            }

            return $"{prefixService.GetNegativePrefix()}{baseForm}";
        }
    }
}