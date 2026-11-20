// ToolLending.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;

    /// <summary>Lending an owned tool: only its owner can lend it, only to someone else, and only while it is not already lent; the tool is handed over into the borrower's hand, who may use it (<see cref="WorkObjects.CanUse"/>) and puts it down at its shed, which ends the loan.</summary>
    public static class ToolLending
    {
        /// <summary>Lends the tool to the borrower, who takes it in hand where they stand; false (nothing changes) when it is unknown, not owned by <paramref name="owner"/>, already lent, held by someone other than the owner (the owner may hand on a tool they hold), or the borrower is the owner.</summary>
        public static bool Lend(IMutableWorldObjectProvider provider, string toolId, HumanId owner, HumanId borrower, string borrowerLocation, GameEngineTools.World.Utils.Time.WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { } tool || tool.OwnerId != owner || tool.LentTo is not null || (tool.HeldBy is not null && tool.HeldBy != owner) || borrower == owner)
            {
                return false;
            }

            provider.RemoveObject(tool.LocationId, tool.Id);
            provider.AddObject(tool with { LentTo = borrower, LentAt = now, HeldBy = borrower, LocationId = borrowerLocation });
            return true;
        }

        /// <summary>Ends the loan by the borrower handing the tool to its owner where both stand; at the tool's own shed it is put back there, anywhere else the owner holds it (<c>LentAt</c> keeps the hour of the hand-over) and carries it to its shed. False when the tool is not lent to <paramref name="borrower"/> or is not in their hand.</summary>
        public static bool HandBack(IMutableWorldObjectProvider provider, string toolId, HumanId borrower, string location, GameEngineTools.World.Utils.Time.WDateTime now)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { LentTo: { } lentTo, OwnerId: { } owner } tool || lentTo != borrower || tool.HeldBy != borrower)
            {
                return false;
            }

            provider.RemoveObject(tool.LocationId, tool.Id);

            // Handed over at its own shed the tool is simply put back there; elsewhere the owner holds it and carries it on.
            var atShed = string.Equals(tool.StoredAt, location, StringComparison.Ordinal);
            provider.AddObject(tool with { LentTo = null, LentAt = atShed ? null : now, HeldBy = atShed ? null : owner, LocationId = location });
            return true;
        }

        /// <summary>Whether the tool, held by <paramref name="holder"/>, is in its owner's hand after a hand-back (not lent, <c>LentAt</c> still set): it is carried to its shed, not kept.</summary>
        public static bool IsHandedBack(WorldObject tool, HumanId holder)
        {
            ArgumentNullException.ThrowIfNull(tool);

            // The holder is passed in: a list of a person's held objects does not carry HeldBy.
            return tool.LentTo is null && tool.LentAt is not null && tool.OwnerId == holder;
        }

        /// <summary>The holder puts the tool down where they stand; it lies there, free to be lent or taken again. False when the tool is unknown or not in <paramref name="holder"/>'s hand.</summary>
        public static bool SetDown(IMutableWorldObjectProvider provider, string toolId, HumanId holder, string location)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { } tool || tool.HeldBy != holder)
            {
                return false;
            }

            provider.RemoveObject(tool.LocationId, tool.Id);
            provider.AddObject(tool with { LentTo = null, LentAt = null, HeldBy = null, LocationId = location });
            return true;
        }

        /// <summary>Takes the loan back: either the owner or the borrower can end it; false when the tool is not lent or <paramref name="by"/> is neither.</summary>
        public static bool Return(IMutableWorldObjectProvider provider, string toolId, HumanId by)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { LentTo: { } borrower } tool || (by != tool.OwnerId && by != borrower))
            {
                return false;
            }

            // An owner who takes it back from the borrower's hand puts it in its shed; a tool the borrower already put down stays where it lies.
            provider.RemoveObject(tool.LocationId, tool.Id);
            provider.AddObject(tool with { LentTo = null, LentAt = null, HeldBy = null, LocationId = tool.HeldBy is null ? tool.LocationId : tool.StoredAt ?? tool.LocationId });
            return true;
        }
    }
}
