// IClock.cs
// Copyright (c) 50PSoftware

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
