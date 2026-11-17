using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using NAudio.Wave.SampleProviders;
using Spectralis.Core.Visualizers;

// Diagnostics, not a shared-runner frame-rate gate: report real work and allocations.
var provider = new VisualizerSampleProvider(new SignalGenerator(48000, 2) { Frequency = 440, Gain = .5 });
var samples = new float[1024];
for (var i = 0; i < 100; i++) provider.Read(samples, 0, samples.Length);
var watch = Stopwatch.StartNew();
for (var i = 0; i < 3000; i++) provider.Read(samples, 0, samples.Length);
watch.Stop();
provider.GetFrame(includeSpectrogram: true);
var before = GC.GetAllocatedBytesForCurrentThread();
for (var i = 0; i < 60; i++) provider.GetFrame(includeSpectrogram: true);
var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
Console.WriteLine(JsonSerializer.Serialize(new { architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    audioSeconds = 32, analysisMilliseconds = watch.Elapsed.TotalMilliseconds, spectrogram60FramesAllocated = allocated }));
