using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grammar.Logic;

namespace Grammar.Services
{
    public class MorphologyService : IMorphologyService
    {
        private readonly DeclensionEngine engine;

        public MorphologyService()
        {
            throw new NotImplementedException();
            engine = new DeclensionEngine("Data");
        }
    }
}
