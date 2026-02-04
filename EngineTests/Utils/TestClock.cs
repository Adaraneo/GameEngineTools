using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using GameEngineTools;
using GameEngineTools.World.Core.Time;
using GameEngineTools.World.Utils.Time;

namespace EngineTests.Utils
{
    internal class TestClock : IClock
    {
        private readonly System.Timers.Timer timer;
        public TestClock(IWorldClock worldClock)
        {
            timer = new System.Timers.Timer();
            timer.Interval = worldClock.TimeScale * 10;
            timer.Elapsed += Timer_Elapsed;
            Now = WDateTime.Now;
        }

        private void Timer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Advance(WTimeSpan.FromSeconds(1));
        }

        public void Start()
        {
            timer.Start();
        }
        public void Stop()
        {
            timer.Stop();
        }
        public WDateTime Now { get; private set; }
        public void Advance(WTimeSpan timeSpan)
        {
            Now = Now.Add(timeSpan);
        }
    }
}
