// TestWork.cs
// Copyright (c) 50PSoftware

namespace EngineTests.Utils
{
    using GameEngineTools.World.Work;

    /// <summary>The built-in work catalog (materials, object types, tasks), loaded once for the tests that need items, recipes or prices.</summary>
    internal static class TestWork
    {
        public static readonly WorkCatalog Catalog = WorkCatalogLoader.Load();
    }
}
