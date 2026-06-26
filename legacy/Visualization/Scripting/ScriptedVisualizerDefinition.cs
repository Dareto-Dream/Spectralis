namespace Spectralis;

internal sealed class ScriptedVisualizerDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Untitled";
    public string Script { get; set; } = DefaultScript;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public const string DefaultScript =
        "// Spectrum bars\n" +
        "ctx.setFill('#8844ff');\n" +
        "for (let i = 0; i < scene.spectrum.length; i++) {\n" +
        "  let h = scene.spectrum[i] * scene.height;\n" +
        "  ctx.fillRect(i / scene.spectrum.length * scene.width, scene.height - h, scene.width / scene.spectrum.length - 1, h);\n" +
        "}";
}
