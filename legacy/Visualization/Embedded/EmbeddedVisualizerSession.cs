using System.Diagnostics;
using Wasmtime;

namespace Spectralis;

internal sealed class EmbeddedVisualizerSession : IDisposable
{
    private const ulong FuelBudget = 1_200_000;
    private const int MaxMemoryBytes = 2 * 1024 * 1024;

    private readonly EmbeddedVisualizerContext context;
    private readonly EmbeddedVisualizerStyle fallbackStyle;
    private readonly EmbeddedVisualizerHostState hostState;
    private readonly Engine engine;
    private readonly Store store;
    private readonly Linker linker;
    private readonly Module module;
    private readonly Instance instance;

    private readonly Action<float, float, float>? instructionEntry3;
    private readonly Action<float>? instructionEntry1;
    private readonly Action? instructionEntry0;
    private readonly Func<float, float, float, float>? scalarEntry3;
    private readonly Func<float, float>? scalarEntry1;
    private readonly Func<float>? scalarEntry0;

    private bool isFaulted;

    private EmbeddedVisualizerSession(EmbeddedVisualizerContext context)
    {
        this.context = context;
        fallbackStyle = EmbeddedVisualizerStyle.FromContext(context);
        hostState = new EmbeddedVisualizerHostState(fallbackStyle);
        engine = CreateEngine();
        store = new Store(engine, hostState);
        store.SetLimits(memorySize: MaxMemoryBytes, tableElements: 256, instances: 1, tables: 8, memories: 2);

        linker = new Linker(engine);
        DefineHostApi(linker, "delta");
        DefineHostApi(linker, "env");

        module = CreateModule(engine, context);
        instance = linker.Instantiate(store, module);

        instructionEntry3 = instance.GetAction<float, float, float>(context.Module.Entry);
        instructionEntry1 = instance.GetAction<float>(context.Module.Entry);
        instructionEntry0 = instance.GetAction(context.Module.Entry);
        scalarEntry3 = instance.GetFunction<float, float, float, float>(context.Module.Entry);
        scalarEntry1 = instance.GetFunction<float, float>(context.Module.Entry);
        scalarEntry0 = instance.GetFunction<float>(context.Module.Entry);

        if (instructionEntry3 is null &&
            instructionEntry1 is null &&
            instructionEntry0 is null &&
            scalarEntry3 is null &&
            scalarEntry1 is null &&
            scalarEntry0 is null)
        {
            throw new InvalidOperationException(
                $"Embedded visualizer entry '{context.Module.Entry}' does not match a supported WASM signature.");
        }
    }

    public string DisplayLabel => context.DisplayLabel;

    public bool IsFaulted => isFaulted;

    public static EmbeddedVisualizerSession? TryCreate(EmbeddedVisualizerContext? context)
    {
        if (context is null)
        {
            return null;
        }

        try
        {
            return new EmbeddedVisualizerSession(context);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Embedded visualizer '{context.Module.Id}' failed to initialize: {ex}");
            return null;
        }
    }

    public IReadOnlyList<EmbeddedDrawInstruction> Render(VisualizerScene scene)
    {
        if (isFaulted)
        {
            return Array.Empty<EmbeddedDrawInstruction>();
        }

        hostState.BeginFrame(scene);

        try
        {
            store.Fuel = FuelBudget;
            InvokeEntry(scene);
            return hostState.CreateSnapshot();
        }
        catch (Exception ex)
        {
            isFaulted = true;
            Debug.WriteLine($"Embedded visualizer '{context.Module.Id}' failed during render: {ex}");
            return Array.Empty<EmbeddedDrawInstruction>();
        }
    }

    public void Dispose()
    {
        module.Dispose();
        linker.Dispose();
        store.Dispose();
        engine.Dispose();
    }

    private void InvokeEntry(VisualizerScene scene)
    {
        if (instructionEntry3 is not null)
        {
            instructionEntry3(scene.PlaybackTimeSeconds, scene.PeakLevel, scene.RmsLevel);
            return;
        }

        if (instructionEntry1 is not null)
        {
            instructionEntry1(scene.PlaybackTimeSeconds);
            return;
        }

        if (instructionEntry0 is not null)
        {
            instructionEntry0();
            return;
        }

        if (scalarEntry3 is not null)
        {
            RenderScalar(scene, time => scalarEntry3(time, scene.PeakLevel, scene.RmsLevel));
            return;
        }

        if (scalarEntry1 is not null)
        {
            RenderScalar(scene, scalarEntry1);
            return;
        }

        if (scalarEntry0 is not null)
        {
            RenderScalar(scene, _ => scalarEntry0());
        }
    }

    private void RenderScalar(VisualizerScene scene, Func<float, float> sampler)
    {
        var amplitude = Math.Clamp(
            (fallbackStyle.Amplitude / 100f) * (scene.IsActive ? 0.18f + (scene.RmsLevel * 0.34f) : 0.16f),
            0.08f,
            0.42f);
        var phaseOffset = scene.AnimationPhase * 0.012f;
        var timespanSeconds = 2.6f;

        var previousX = 0f;
        var previousY = 0.5f - (NormalizeScalar(sampler(scene.PlaybackTimeSeconds + phaseOffset)) * amplitude);

        for (var index = 1; index < fallbackStyle.SampleCount; index++)
        {
            var x = index / (fallbackStyle.SampleCount - 1f);
            var sampleTime = scene.PlaybackTimeSeconds + phaseOffset + (x * timespanSeconds);
            var sample = NormalizeScalar(sampler(sampleTime));
            var y = 0.5f - (sample * amplitude);
            hostState.AddLine(previousX, previousY, x, y);
            previousX = x;
            previousY = y;
        }
    }

    private static float NormalizeScalar(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, -1f, 1f);
    }

    private static Engine CreateEngine()
    {
        using var config = new Config()
            .WithFuelConsumption(true)
            .WithMaximumStackSize(128 * 1024);

        return new Engine(config);
    }

    private static Module CreateModule(Engine engine, EmbeddedVisualizerContext context)
    {
        if (LooksLikeWat(context.Binary, out var watText))
        {
            return Module.FromText(engine, context.Module.Id, watText);
        }

        return Module.FromBytes(engine, context.Module.Id, context.Binary);
    }

    private static bool LooksLikeWat(byte[] binary, out string text)
    {
        text = string.Empty;

        try
        {
            text = System.Text.Encoding.UTF8.GetString(binary).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            return text.StartsWith("(module", StringComparison.Ordinal);
        }
        catch
        {
            text = string.Empty;
            return false;
        }
    }

    private static void DefineHostApi(Linker linker, string moduleName)
    {
        linker.DefineFunction(moduleName, "set_color", (Caller caller, int red, int green, int blue, int alpha) =>
            GetState(caller).SetColor(red, green, blue, alpha));
        linker.DefineFunction(moduleName, "set_thickness", (Caller caller, float thickness) =>
            GetState(caller).SetThickness(thickness));
        linker.DefineFunction(moduleName, "line", (Caller caller, float x1, float y1, float x2, float y2) =>
            GetState(caller).AddLine(x1, y1, x2, y2));
        linker.DefineFunction(moduleName, "rect", (Caller caller, float x, float y, float width, float height, int filled) =>
            GetState(caller).AddRectangle(x, y, width, height, filled != 0));
        linker.DefineFunction(moduleName, "circle", (Caller caller, float centerX, float centerY, float radius, int filled) =>
            GetState(caller).AddCircle(centerX, centerY, radius, filled != 0));
        linker.DefineFunction(moduleName, "get_time", (Caller caller) => GetState(caller).PlaybackTimeSeconds);
        linker.DefineFunction(moduleName, "get_peak", (Caller caller) => GetState(caller).PeakLevel);
        linker.DefineFunction(moduleName, "get_rms", (Caller caller) => GetState(caller).RmsLevel);
        linker.DefineFunction(moduleName, "get_spectrum_length", (Caller caller) => GetState(caller).SpectrumLength);
        linker.DefineFunction(moduleName, "get_waveform_length", (Caller caller) => GetState(caller).WaveformLength);
        linker.DefineFunction(moduleName, "get_spectrum", (Caller caller, int index) => GetState(caller).GetSpectrum(index));
        linker.DefineFunction(moduleName, "get_waveform", (Caller caller, int index) => GetState(caller).GetWaveform(index));
    }

    private static EmbeddedVisualizerHostState GetState(Caller caller) =>
        caller.GetData() as EmbeddedVisualizerHostState
        ?? throw new InvalidOperationException("Embedded visualizer host state is unavailable.");
}
