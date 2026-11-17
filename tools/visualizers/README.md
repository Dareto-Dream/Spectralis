# Visualizer captures

From the repository root:

```powershell
dotnet run --project tools/visualizers/CaptureVisualizers.csproj -c Release
```

This runs the actual app renderers against an eight-second deterministic audio passage and captures their output through Avalonia/Skia. The PNGs in `Spectralis.App/Assets/VisualizerCaptures` are the gallery thumbnails. `captures.json` records the source hashes, image hashes, frame dimensions, and appearance so an update can be checked and reproduced. The images are screenshots, with no stock or generated concept art. Artwork modes capture their actual no-artwork state; installed custom visualizers use a labeled fallback rather than an unrelated built-in screenshot.

Regenerate captures whenever a renderer changes, then rebuild the app to embed them. To render full-size review frames, the gallery at 980px and 720px, and measure software drawing time:

```powershell
dotnet run --project tools/visualizers/CaptureVisualizers.csproj -c Release -- --review
```

Review output goes to the ignored `output/world-review` directory. This is a local development tool; it neither runs on application startup nor starts a release.
