using GameEngineTools.World.Utils.Time;

namespace GameEngineTools.World.Core.Time
{
    public interface IClock
    {
        WDateTime Now { get; }

        void Start();

        void Stop();
    }
}
