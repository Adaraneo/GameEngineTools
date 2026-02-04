using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Grammar.Models;

namespace Grammar.Logic
{
    internal class SentenceGenerator
    {
        private readonly DeclensionEngine engine;

        public SentenceGenerator()
        {
            engine = new DeclensionEngine("Data");
        }

        // TODO: SimpleSentencGenerator
    }
}