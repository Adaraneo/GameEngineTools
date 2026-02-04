using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Grammar.Services
{
    public interface ISofteningService
    {
        /// <summary>
        /// Aplikuje měkčení na konci slova, pokud to vyžaduje daná koncovka.
        /// </summary>
        /// <param name="baseWord">Slovo v základním tvaru</param>
        /// <param name="suffix">Koncovka, která bude připojena</param>
        /// <returns>Slovo s případně změkčeným koncem</returns>
        string ApplySofteningIfNeeded(string baseWord, string suffix);

        /// <summary>
        /// Pokusí se vrátit změkčené slovo zpět do původní tvrdé podoby.
        /// </summary>
        string RevertSoftening(string word);
    }
}
