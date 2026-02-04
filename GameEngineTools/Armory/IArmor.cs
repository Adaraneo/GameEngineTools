// IArmor.cs
// Copyright (c) 50PSoftware

namespace GameEngineTools.Armory
{
    public interface IArmor
    {
        double MaxProtection { get; }
        string Name { get; }
        double Protection { get; }
    }
}
