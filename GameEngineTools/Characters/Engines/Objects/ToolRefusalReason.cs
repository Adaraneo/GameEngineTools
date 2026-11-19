// ToolRefusalReason.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Objects
{
    /// <summary>Why an owner refused to lend a tool, for a monitor to tell the causes apart.</summary>
    public enum ToolRefusalReason
    {
        /// <summary>The owner works and the tool lies where the owner stands.</summary>
        NeededNow,

        /// <summary>The owner has no standing view of the asker (a stranger) or holds them in contempt.</summary>
        NoStanding,

        /// <summary>The owner's feeling for the asker is below the owner's threshold, so the chance was under one half.</summary>
        BelowThreshold,

        /// <summary>The feeling was enough (chance of one half or more) but the draw went against the asker.</summary>
        Unlucky,
    }
}
