// PerceivedObject.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Characters.Engines.Memory
{
    using GameEngineTools.World.Objects;

    /// <summary>One object a character noticed at a place: its id, its catalog type (if any) and its category.</summary>
    public sealed record PerceivedObject(string ObjectId, string? TypeId, WorldObjectCategory Category);
}
