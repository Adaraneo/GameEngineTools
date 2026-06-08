// IArmor.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    /// <summary>Common contract for armour items: a name plus current and maximum protection.</summary>
    public interface IArmor
    {
        /// <summary>Maximum protection this item can provide when undamaged.</summary>
        double MaxProtection { get; }
        /// <summary>Display name.</summary>
        string Name { get; }
        /// <summary>Current protection value (≤ <see cref="MaxProtection"/>).</summary>
        double Protection { get; }
    }
}
