using Spectralis.Core.Worlds;
using Wasmtime;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>
/// Exercises <see cref="WasmWorldHost"/> against real, hand-written WAT modules compiled via
/// <see cref="Module.ConvertText"/> — the same "define host imports, call into a real wasm
/// module, verify the trip" shape proven manually before this class existed.
/// </summary>
public sealed class WasmWorldHostTests : IDisposable
{
    private readonly string _storeKey = $"test-world-{Guid.NewGuid():N}";
    private readonly WasmWorldHost _host;

    public WasmWorldHostTests() => _host = new WasmWorldHost(_storeKey);

    public void Dispose()
    {
        _host.Dispose();
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "capsule-store", _storeKey + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static byte[] Wat(string source) => Module.ConvertText(source);

    [Fact]
    public void Load_InvalidBytes_ReturnsFalseAndStaysUnloaded()
    {
        var ok = _host.Load([0x00, 0x01, 0x02]);

        Assert.False(ok);
        Assert.False(_host.IsLoaded);
    }

    [Fact]
    public void Load_ModuleWithNoExports_SucceedsAsNoOp()
    {
        var wasm = Wat("(module)");

        var ok = _host.Load(wasm);

        Assert.True(ok);
        Assert.True(_host.IsLoaded);
    }

    [Fact]
    public void OnLoad_PlayTrackAndUnlockAchievement_RaiseEvents()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "play_track" (func $play_track (param i32 i32 f64)))
              (import "spectral" "unlock_achievement" (func $unlock_achievement (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "song-1")
              (data (i32.const 16) "first-boot")
              (func (export "on_load")
                (call $play_track (i32.const 0) (i32.const 6) (f64.const 12.5))
                (call $unlock_achievement (i32.const 16) (i32.const 10))))
            """);

        Spectralis.Core.Integrations.Web.AlbumTrackPlayRequest? playRequest = null;
        string? achievement = null;
        _host.PlayTrackRequested += (_, e) => playRequest = e;
        _host.AchievementUnlocked += (_, e) => achievement = e;

        var ok = _host.Load(wasm);

        Assert.True(ok);
        Assert.NotNull(playRequest);
        Assert.Equal("song-1", playRequest!.TrackId);
        Assert.Equal(12.5, playRequest.PositionSeconds);
        Assert.Equal("first-boot", achievement);
    }

    [Fact]
    public void OnLoad_SaveBookmark_RaisesEventWithClampedLabel()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "save_bookmark" (func $save_bookmark (param i32 i32 f64 i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "track-9")
              (data (i32.const 16) "chapter 2")
              (func (export "on_load")
                (call $save_bookmark (i32.const 0) (i32.const 7) (f64.const 3.0) (i32.const 16) (i32.const 9))))
            """);

        Spectralis.Core.Integrations.Web.AlbumBookmarkRequest? request = null;
        _host.SaveBookmarkRequested += (_, e) => request = e;

        Assert.True(_host.Load(wasm));

        Assert.NotNull(request);
        Assert.Equal("track-9", request!.TrackId);
        Assert.Equal(3.0, request.PositionSeconds);
        Assert.Equal("chapter 2", request.Label);
    }

    [Fact]
    public void OnLoad_RegisterThenReleaseDspPreset_RaisesBothEvents()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "register_dsp_preset" (func $register (param i32 i32)))
              (import "spectral" "release_dsp_preset" (func $release))
              (memory (export "memory") 1)
              (data (i32.const 0) "{\22Enabled\22:true,\22Effects\22:[]}")
              (func (export "on_load")
                (call $register (i32.const 0) (i32.const 29))
                (call $release)))
            """);

        string? presetJson = null;
        var released = 0;
        _host.DspPresetRegisterRequested += (_, e) => presetJson = e.PresetChainJson;
        _host.DspPresetReleaseRequested += (_, _) => released++;

        Assert.True(_host.Load(wasm));

        Assert.Equal("""{"Enabled":true,"Effects":[]}""", presetJson);
        Assert.Equal(1, released);
    }

    [Fact]
    public void OnLoad_SwitchToHtml_RaisesEventWithEntryPoint()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "switch_to_html" (func $switch (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "bonus.html")
              (func (export "on_load")
                (call $switch (i32.const 0) (i32.const 10))))
            """);

        string? entry = null;
        _host.SwitchToHtmlRequested += (_, e) => entry = e;

        Assert.True(_host.Load(wasm));

        Assert.Equal("bonus.html", entry);
    }

    [Fact]
    public void StorageSetThenGet_RoundTripsThroughTheSharedFile()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "storage_set" (func $set (param i32 i32 i32 i32)))
              (import "spectral" "storage_get" (func $get (param i32 i32 i32 i32) (result i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "pos")
              (data (i32.const 16) "\2242\22")
              (func (export "on_load")
                (call $set (i32.const 0) (i32.const 3) (i32.const 16) (i32.const 4))
                (drop (call $get (i32.const 0) (i32.const 3) (i32.const 64) (i32.const 16)))))
            """);

        Assert.True(_host.Load(wasm));

        // The scoped store persists across host instances keyed by the same storeKey — this is
        // exactly what lets a runtime switch (Wasm <-> HTML) carry hand-off state.
        var independentStore = new Spectralis.Core.Capsule.CapsuleScopedStore(_storeKey);
        var stored = independentStore.Get("pos");
        Assert.NotNull(stored);
        Assert.Equal("42", stored!.GetValue<string>());
    }

    [Fact]
    public void Tick_BeforeLoad_IsANoOp()
    {
        _host.Tick(1.0, true); // must not throw
        Assert.False(_host.IsLoaded);
    }

    [Fact]
    public void Tick_RunawayLoop_TrapsWithoutCrashingTheHost()
    {
        var wasm = Wat("""
            (module
              (func (export "on_tick") (param f64 i32)
                (local $i i32)
                (local.set $i (i32.const 0))
                (block $exit
                  (loop $loop
                    (local.set $i (i32.add (local.get $i) (i32.const 1)))
                    (br_if $exit (i32.ge_u (local.get $i) (i32.const 2000000000)))
                    (br $loop)))))
            """);

        Assert.True(_host.Load(wasm));

        // Must not throw — a trap during Tick is swallowed (dropped frame), not fatal.
        _host.Tick(5.0, true);
        Assert.True(_host.IsLoaded);
    }

    [Fact]
    public void Unload_CallsOnUnloadThenDetaches()
    {
        var wasm = Wat("""
            (module
              (import "spectral" "unlock_achievement" (func $unlock (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "goodbye")
              (func (export "on_unload")
                (call $unlock (i32.const 0) (i32.const 7))))
            """);
        Assert.True(_host.Load(wasm));

        string? achievement = null;
        _host.AchievementUnlocked += (_, e) => achievement = e;

        _host.Unload();

        Assert.Equal("goodbye", achievement);
        Assert.False(_host.IsLoaded);
    }
}
