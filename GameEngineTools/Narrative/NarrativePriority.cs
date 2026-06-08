// NarrativePriority.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Narrative
{
    /// <summary>
    /// Priority of a narrative entry — determines how important the event is to the player.
    /// </summary>
    /// <remarks>
    /// Used for filtering: show only <see cref="High"/> and <see cref="Medium"/>
    /// in the game UI, and <see cref="Low"/> only in the debug journal.
    /// </remarks>
    public enum NarrativePriority
    {
        /// <summary>
        /// Everyday routine — eating, sleeping, resting, self-care.
        /// Show only in detailed journal mode.
        /// </summary>
        Low,

        /// <summary>
        /// A socially or emotionally interesting event.
        /// Suitable for the game UI (dialog box, floating message, journal entry).
        /// </summary>
        Medium,

        /// <summary>
        /// A pivotal moment — first impression, intimacy rejection, nightmare, reconciliation.
        /// Deserves highlighting, animation, or a notification.
        /// </summary>
        High
    }
}
