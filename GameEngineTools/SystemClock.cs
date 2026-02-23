// SystemClock.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools
{
    using System;
    using System.Timers;
    using GameEngineTools.World.Core.Time;
    using GameEngineTools.World.Utils.Time;

    public sealed class SystemClock : IClock, IDisposable
    {
        private Timer timer;
        private readonly double timeScale;

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Now = Now.AddSeconds(timeScale);
        }

        public SystemClock(IWorldClock worldClock)
        {
            this.timeScale = worldClock.TimeScale;
            this.timer = new Timer();
            this.timer.Interval = 1000;
            this.timer.Elapsed += Timer_Elapsed;
        }

        public WDateTime Now { get; private set; } = WDateTime.Now;

        public void Dispose()
        {
            this.timer.Stop();
            this.timer.Dispose();
        }

        public void SetNow(WDateTime now)
        {
            Now = now;
        }

        public void Advance(WTimeSpan dt)
        {
            Now = Now.Add(dt);
        }

        public void Start()
        {
            this.timer.Start();
        }

        public void Stop()
        {
            this.timer.Stop();
        }
    }
}
