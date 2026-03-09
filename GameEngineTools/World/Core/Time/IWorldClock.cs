// IWorldClock.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Core.Time
{
    public interface IWorldClock
    {
        double TimeScale { get; }

        internal long NowWorldTicks();
    }
}
