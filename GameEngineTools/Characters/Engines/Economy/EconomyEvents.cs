// EconomyEvents.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Economy
{
    using GameEngineTools.Characters.Core;
    using GameEngineTools.World.Objects;
    using GameEngineTools.World.Utils.Time;

    /// <summary>
    /// Fired when a character earns a wage for time worked. Consumed by
    /// <see cref="DefaultEconomyEngine"/> is <b>not</b> required — the engine applies the wage itself
    /// and emits this as a notification for logging and observers. Food-economy Tier 2.
    /// </summary>
    public sealed record WageEarned(
        WDateTime OccurredAt,
        HumanId Actor,
        string Occupation,
        double WagePerHour,
        double HoursWorked,
        double NewWealth) : IDomainEvent;

    /// <summary>
    /// Fired by the object-interaction commit path when a character buys a priced object from a shop.
    /// <see cref="DefaultEconomyEngine"/> consumes it to deduct <see cref="Price"/> from wealth. Tier 2.
    /// </summary>
    public sealed record Purchased(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        PickupItemKind ItemKind,
        string ShopId,
        double Price,
        double NewWealth) : IDomainEvent;

    /// <summary>
    /// Fired by the object-interaction commit path when a character sells a held item back to a shop.
    /// <see cref="DefaultEconomyEngine"/> consumes it to credit <see cref="Price"/> to wealth. Tier 2.
    /// </summary>
    public sealed record Sold(
        WDateTime OccurredAt,
        HumanId Actor,
        string ObjectId,
        PickupItemKind ItemKind,
        string ShopId,
        double Price,
        double NewWealth) : IDomainEvent;

    /// <summary>
    /// Fired when a shop's posted price for an item kind changes after a buy/sell/restock. Purely a
    /// notification for logging and observers — the authoritative price lives in the scene's
    /// <c>EconomyLedger</c>. Food-economy Tier 2.
    /// </summary>
    public sealed record PriceChanged(
        WDateTime OccurredAt,
        HumanId Actor,
        string ShopId,
        PickupItemKind ItemKind,
        double OldPrice,
        double NewPrice,
        int NewStockCount) : IDomainEvent;
}
