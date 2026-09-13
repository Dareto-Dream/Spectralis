using System.Text.Json.Nodes;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class CapsuleScopedStoreTests : IDisposable
{
    private readonly string _storeKey = $"test-store-{Guid.NewGuid():N}";
    private readonly string _filePath;

    public CapsuleScopedStoreTests()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "capsule-store", _storeKey + ".json");
    }

    public void Dispose()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    [Fact]
    public void SetThenGet_RoundTrips()
    {
        var store = new CapsuleScopedStore(_storeKey);

        Assert.True(store.Set("pos", JsonValue.Create("42")));

        Assert.Equal("42", store.Get("pos")!.GetValue<string>());
    }

    [Fact]
    public void SeparateInstancesSameKey_ShareTheSameFile()
    {
        var writer = new CapsuleScopedStore(_storeKey);
        writer.Set("achievements", JsonValue.Create("first-boot"));

        // A fresh instance with the same storeKey — exactly what happens when a runtime switch
        // (Wasm <-> HTML) constructs a new host/service for the same world.
        var reader = new CapsuleScopedStore(_storeKey);

        Assert.Equal("first-boot", reader.Get("achievements")!.GetValue<string>());
    }

    [Fact]
    public void Get_UnknownKey_ReturnsNull()
    {
        var store = new CapsuleScopedStore(_storeKey);

        Assert.Null(store.Get("nope"));
    }

    [Fact]
    public void Remove_DeletesTheKey()
    {
        var store = new CapsuleScopedStore(_storeKey);
        store.Set("k", JsonValue.Create(1));

        Assert.True(store.Remove("k"));
        Assert.Null(store.Get("k"));
        Assert.False(store.Remove("k")); // already gone
    }

    [Fact]
    public void Clear_RemovesEverything()
    {
        var store = new CapsuleScopedStore(_storeKey);
        store.Set("a", JsonValue.Create(1));
        store.Set("b", JsonValue.Create(2));

        store.Clear();

        Assert.Null(store.Get("a"));
        Assert.Null(store.Get("b"));
    }

    [Fact]
    public void Set_OversizedKey_IsRejected()
    {
        var store = new CapsuleScopedStore(_storeKey);
        var oversized = new string('x', CapsuleScopedStore.MaxKeyBytes + 1);

        Assert.False(store.Set(oversized, JsonValue.Create(1)));
        Assert.Null(store.Get(oversized));
    }
}
