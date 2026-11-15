// ToolLending.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.World.Work
{
    using System;
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;

    /// <summary>Lending an owned tool: only its owner can lend it, only to someone else, and only while it is not already lent; the borrower may then use it (<see cref="WorkObjects.CanUse"/>).</summary>
    public static class ToolLending
    {
        /// <summary>Lends the tool to the borrower; false (nothing changes) when it is unknown, not owned by <paramref name="owner"/>, already lent, or the borrower is the owner.</summary>
        public static bool Lend(IMutableWorldObjectProvider provider, string toolId, HumanId owner, HumanId borrower)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { } tool || tool.OwnerId != owner || tool.LentTo is not null || borrower == owner)
                return false;

            provider.RemoveObject(tool.LocationId, tool.Id);
            provider.AddObject(tool with { LentTo = borrower });
            return true;
        }

        /// <summary>Takes the loan back: either the owner or the borrower can end it; false when the tool is not lent or <paramref name="by"/> is neither.</summary>
        public static bool Return(IMutableWorldObjectProvider provider, string toolId, HumanId by)
        {
            ArgumentNullException.ThrowIfNull(provider);
            if (provider.FindObject(toolId) is not { LentTo: { } borrower } tool || (by != tool.OwnerId && by != borrower))
                return false;

            provider.RemoveObject(tool.LocationId, tool.Id);
            provider.AddObject(tool with { LentTo = null });
            return true;
        }
    }
}
