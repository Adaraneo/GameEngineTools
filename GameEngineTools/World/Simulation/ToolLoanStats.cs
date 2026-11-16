// ToolLoanStats.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Simulation
{
    /// <summary>How the tool loans of a scene have gone so far, for a monitor to report.</summary>
    public sealed class ToolLoanStats
    {
        /// <summary>Requests an owner answered with a loan.</summary>
        public int Lent { get; internal set; }

        /// <summary>Requests an owner refused.</summary>
        public int Refused { get; internal set; }

        /// <summary>Lent tools put back by the borrower.</summary>
        public int Returned { get; internal set; }

        /// <summary>Loans reported overdue (once per day each).</summary>
        public int Overdue { get; internal set; }

        /// <summary>Searches that found no tool to take or borrow anywhere.</summary>
        public int SearchesFailed { get; internal set; }
    }
}
