using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameEngineTools.World.Core.Time
{
    public interface IWorldClock
    {
        double TimeScale { get; }
    }
}
