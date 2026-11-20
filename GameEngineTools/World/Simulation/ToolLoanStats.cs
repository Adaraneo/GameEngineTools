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

        /// <summary>Refusals because the owner needed the tool where the owner stood.</summary>
        public int RefusedNeededNow { get; internal set; }

        /// <summary>Refusals because the owner had no standing view of the asker.</summary>
        public int RefusedNoStanding { get; internal set; }

        /// <summary>Refusals because the owner's feeling for the asker was below the owner's threshold.</summary>
        public int RefusedBelowThreshold { get; internal set; }

        /// <summary>Refusals with a chance of one half or more where the draw went against the asker.</summary>
        public int RefusedUnlucky { get; internal set; }

        /// <summary>Lent tools put back by the borrower.</summary>
        public int Returned { get; internal set; }

        /// <summary>Lent tools the borrower handed to the owner where both stood.</summary>
        public int HandedBack { get; internal set; }

        /// <summary>Tools an owner noticed lying away from their shed and took back.</summary>
        public int TakenBack { get; internal set; }

        /// <summary>Handed-back tools an owner put back into their shed on arriving there.</summary>
        public int PutAway { get; internal set; }

        /// <summary>Handed-back tools an owner put down where he stood because their shed was too far to carry them to.</summary>
        public int SetDownAway { get; internal set; }

        /// <summary>Walks back to a shed that were not begun because the shed was too far to afford.</summary>
        public int ReturnsDeferred { get; internal set; }

        /// <summary>Loans reported overdue (once per day each).</summary>
        public int Overdue { get; internal set; }

        /// <summary>Searches that found no tool to take or borrow anywhere.</summary>
        public int SearchesFailed { get; internal set; }

        /// <summary>Requests spoken to an owner who was present.</summary>
        public int Asked { get; internal set; }

        /// <summary>Walks begun toward a remembered tool, a tool lying elsewhere or an owner.</summary>
        public int Walks { get; internal set; }
    }
}
