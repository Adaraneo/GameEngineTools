using TerrainEditor.Services;

namespace TerrainEditorTests;

[TestClass]
public class LruCacheTests
{
    [TestMethod]
    public void Set_ThenTryGetValue_ReturnsWhatWasStored()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);

        Assert.IsTrue(cache.TryGetValue("a", out var value));
        Assert.AreEqual(1, value);
    }

    [TestMethod]
    public void TryGetValue_MissingKey_ReturnsFalse()
    {
        var cache = new LruCache<string, int>(3);

        Assert.IsFalse(cache.TryGetValue("missing", out _));
    }

    [TestMethod]
    public void Set_BeyondCapacity_EvictsLeastRecentlyUsed()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("c", 3); // evicts "a" — it was inserted first and never touched again

        Assert.IsFalse(cache.TryGetValue("a", out _));
        Assert.IsTrue(cache.TryGetValue("b", out _));
        Assert.IsTrue(cache.TryGetValue("c", out _));
        Assert.AreEqual(2, cache.Count);
    }

    [TestMethod]
    public void TryGetValue_RefreshesRecency_SoItSurvivesEviction()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.TryGetValue("a", out _); // touch "a" — now "b" is the least recently used
        cache.Set("c", 3); // should evict "b", not "a"

        Assert.IsTrue(cache.TryGetValue("a", out _));
        Assert.IsFalse(cache.TryGetValue("b", out _));
        Assert.IsTrue(cache.TryGetValue("c", out _));
    }

    [TestMethod]
    public void Set_ExistingKey_UpdatesValueWithoutEvicting()
    {
        var cache = new LruCache<string, int>(2);
        cache.Set("a", 1);
        cache.Set("b", 2);
        cache.Set("a", 99); // re-set, not a new key

        Assert.AreEqual(2, cache.Count);
        Assert.IsTrue(cache.TryGetValue("a", out var value));
        Assert.AreEqual(99, value);
        Assert.IsTrue(cache.TryGetValue("b", out _));
    }

    [TestMethod]
    public void Clear_RemovesEverything()
    {
        var cache = new LruCache<string, int>(3);
        cache.Set("a", 1);
        cache.Set("b", 2);

        cache.Clear();

        Assert.AreEqual(0, cache.Count);
        Assert.IsFalse(cache.TryGetValue("a", out _));
    }

    [TestMethod]
    public void Constructor_NonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LruCache<string, int>(0));
    }
}
