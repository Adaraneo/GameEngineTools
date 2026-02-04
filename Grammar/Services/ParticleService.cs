using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Grammar.Models;

namespace Grammar.Services
{
    public class ParticleService
    {
        private readonly ParticlesData data;

        public ParticleService(string pathToSourceFilesFolder)
        {
            var json = File.ReadAllText(Path.Combine(pathToSourceFilesFolder, "particles.json"));
            data = JsonSerializer.Deserialize<ParticlesData>(json, Program.SerializerOptions)!;
        }

        public string GetConditionalParticle(GrammaticalNumber number, int person)
        {
            var section = number == GrammaticalNumber.Singular ? data.Conditional.Singular : data.Conditional.Plural;
            return section[person.ToString()];
        }

        public string GetReflexive(bool isDative = false)
        {
            return isDative ? data.Reflexive.Dative : data.Reflexive.Standart;
        }
    }
}
