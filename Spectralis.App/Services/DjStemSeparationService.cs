using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Dj;
using Spectralis.Core.Integrations;

namespace Spectralis.App.Services;

/// <summary>Optional, isolated CPU Demucs installation. No constructor or status probe downloads anything.</summary>
public sealed class DjStemSeparationService
{
    private static readonly SemaphoreSlim Operation = new(1, 1);
    public static string InstallRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "dj-ai");
    private readonly string _root;
    private readonly Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> _execute;
    public DjStemSeparationService(string? root = null, Func<string, IReadOnlyList<string>, CancellationToken, Task<ProcessResult>>? execute = null)
    {
        _root = root ?? InstallRoot;
        _execute = execute ?? ((exe, args, ct) => SafeProcessRunner.RunAsync(exe, args, TimeSpan.FromHours(2), cancellationToken: ct, environment: EnvironmentVariables));
    }
    private string Python => Path.Combine(_root, "venv", OperatingSystem.IsWindows() ? "Scripts/python.exe" : "bin/python");
    private string Marker => Path.Combine(_root, "ready-v1");
    private string Checkpoint => Path.Combine(_root, "models", "hub", "checkpoints", "955717e8-8726e21a.th");
    public bool IsInstalled => File.Exists(Python) && File.Exists(Marker) && File.Exists(Checkpoint);
    public bool HasPartialInstallation => Directory.Exists(Path.Combine(_root, "venv")) && !IsInstalled;
    private Dictionary<string, string> EnvironmentVariables => new()
    {
        ["UV_PYTHON_INSTALL_DIR"] = Path.Combine(_root, "python"),
        ["UV_CACHE_DIR"] = Path.Combine(_root, "cache"),
        ["TORCH_HOME"] = Path.Combine(_root, "models"),
        ["PYTHONNOUSERSITE"] = "1",
        ["PYTHONUNBUFFERED"] = "1",
        ["UV_NO_CONFIG"] = "1",
    };

    public async Task InstallAsync(bool optedIn, IProgress<string>? progress, CancellationToken ct)
    {
        RequireOptIn(optedIn);
        await Operation.WaitAsync(ct);
        try
        {
            progress?.Report("Checking the local AI runtime and model…");
            if (await ValidateAsync(ct))
            {
                await File.WriteAllTextAsync(Marker, "demucs=4.0.1; model=htdemucs; uv=0.10.0", ct);
                progress?.Report("AI tools and model verified. Ready to separate."); return;
            }
            if (File.Exists(Marker)) File.Delete(Marker);
            var architecture = RuntimeInformation.ProcessArchitecture;
            if (OperatingSystem.IsWindows() && architecture != Architecture.X64 || OperatingSystem.IsLinux() && architecture != Architecture.X64)
                throw new PlatformNotSupportedException("Local AI separation currently requires x64 Windows/Linux or a Mac. Prepared stems work on every supported platform.");
            Directory.CreateDirectory(_root);
            using var lease = new FileStream(Path.Combine(_root, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var staging = Path.Combine(_root, "staging");
            Directory.CreateDirectory(staging);
            var windows = OperatingSystem.IsWindows();
            var target = windows ? "x86_64-pc-windows-msvc" : OperatingSystem.IsMacOS()
                ? (architecture == Architecture.Arm64 ? "aarch64-apple-darwin" : "x86_64-apple-darwin") : "x86_64-unknown-linux-gnu";
            var uv = Path.Combine(_root, windows ? "uv.exe" : "uv");
            var uvReady = false;
            if (File.Exists(uv))
                try { var version = await _execute(uv, ["--version"], ct); uvReady = version.Succeeded && version.Stdout.StartsWith("uv 0.10.0", StringComparison.Ordinal); }
                catch (OperationCanceledException) { throw; } catch { }
            if (!uvReady)
            {
                var asset = $"uv-{target}." + (windows ? "zip" : "tar.gz");
                var url = $"https://github.com/astral-sh/uv/releases/download/0.10.0/{asset}";
                var archive = Path.Combine(staging, asset);
                progress?.Report("Downloading the optional AI installer…");
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    response.EnsureSuccessStatusCode();
                    await using var file = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(file, ct);
                }
                var checksum = (await http.GetStringAsync(url + ".sha256", ct)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                await using (var file = File.OpenRead(archive))
                {
                    var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
                    if (!actual.Equals(checksum, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("AI installer checksum did not match.");
                }
                if (windows)
                {
                    using var zip = ZipFile.OpenRead(archive);
                    var entry = zip.Entries.Single(e => e.Name == "uv.exe");
                    await using var source = entry.Open();
                    await using var file = new FileStream(uv, FileMode.Create, FileAccess.Write);
                    await source.CopyToAsync(file, ct);
                }
                else
                {
                    await using var file = File.OpenRead(archive);
                    await using var gzip = new GZipStream(file, CompressionMode.Decompress);
                    using var tar = new TarReader(gzip);
                    TarEntry? entry;
                    var found = false;
                    while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct)) is not null)
                    {
                        if (Path.GetFileName(entry.Name) != "uv" || entry.DataStream is null) continue;
                        await using var output = new FileStream(uv, FileMode.Create, FileAccess.Write);
                        await entry.DataStream.CopyToAsync(output, ct); found = true; break;
                    }
                    if (!found) throw new InvalidDataException("The AI installer archive did not contain uv.");
                    FfmpegLocator.EnsureExecutable(uv);
                }
            }
            progress?.Report("Installing a private Python runtime. This does not change your system Python…");
            // Resume a partial installation without asking uv to recreate its existing venv.
            var pythonWorks = File.Exists(Python) && (await _execute(Python, ["-c", "import sys; print(sys.version)"], ct)).Succeeded;
            if (!pythonWorks)
                await Run(uv, ["venv", "--allow-existing", "--managed-python", "--python", "3.11", Path.Combine(_root, "venv")], ct);
            progress?.Report("Downloading CPU audio inference tools. This may take several minutes…");
            var torchVersion = OperatingSystem.IsMacOS() && architecture == Architecture.X64 ? "2.2.2" : "2.5.1";
            var torchArguments = new List<string> { "pip", "install", "--python", Python, $"torch=={torchVersion}", $"torchaudio=={torchVersion}" };
            if (!OperatingSystem.IsMacOS()) torchArguments.AddRange(["--index-url", "https://download.pytorch.org/whl/cpu"]);
            await Run(uv, torchArguments, ct);
            await Run(uv, ["pip", "install", "--python", Python, "demucs==4.0.1", "numpy==1.26.4", "soundfile==0.13.1"], ct);
            if (File.Exists(Checkpoint))
            {
                await using var checkpoint = File.OpenRead(Checkpoint);
                var valid = Convert.ToHexString(await SHA256.HashDataAsync(checkpoint, ct)).StartsWith("8726E21A", StringComparison.Ordinal);
                await checkpoint.DisposeAsync();
                if (!valid) File.Delete(Checkpoint);
            }
            progress?.Report("Downloading the optional four-stem model…");
            await Run(Python, ["-c", "from demucs.pretrained import get_model; get_model('htdemucs'); print('Model ready')"], ct);
            if (!await ValidateAsync(ct)) throw new InvalidOperationException("The model download or runtime check is incomplete. Choose Install / repair AI tools to resume.");
            await File.WriteAllTextAsync(Marker, "demucs=4.0.1; model=htdemucs; uv=0.10.0", ct);
            progress?.Report("AI tools ready. Choose a song to separate locally.");
        }
        finally
        {
            // Install archives and wheel caches are rebuildable. Keep the runtime and model.
            try
            {
                TempStorageCleanup.DeleteOwnedTree(_root, Path.Combine(_root, "staging"));
                TempStorageCleanup.DeleteOwnedTree(_root, Path.Combine(_root, "cache"));
                if (IsInstalled)
                    foreach (var partial in Directory.EnumerateFiles(Path.GetDirectoryName(Checkpoint)!, "955717e8-8726e21a.th.*.partial"))
                        if ((File.GetAttributes(partial) & FileAttributes.ReparsePoint) == 0) File.Delete(partial);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            finally { Operation.Release(); }
        }
    }

    public async Task<DjSeparatedStems> SeparateAsync(string path, bool optedIn, IProgress<string>? progress, CancellationToken ct)
    {
        RequireOptIn(optedIn);
        await Operation.WaitAsync(ct);
        var root = Path.Combine(Path.GetTempPath(), "Spectralis", "dj-separation");
        var output = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try
        {
            if (!await ValidateAsync(ct))
            {
                if (File.Exists(Marker)) File.Delete(Marker);
                throw new InvalidOperationException("AI installation is incomplete or damaged. Choose Install / repair AI tools to finish it. Separation never downloads a model automatically.");
            }
            using var installLease = new FileStream(Path.Combine(_root, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            Directory.CreateDirectory(output);
            using var lease = new FileStream(Path.Combine(output, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            progress?.Report("Preparing audio for local stem separation…");
            using var decoded = await FfmpegWaveStream.OpenAsync(path, cancellationToken: ct, sampleRate: 44100, channels: 2);
            // Public WAV writer avoids depending on another process's open temp-file sharing flags.
            var input = Path.Combine(output, "input.wav");
            await Task.Run(() => NAudio.Wave.WaveFileWriter.CreateWaveFile(input, decoded), ct);
            progress?.Report("Separating vocals, drums, bass and other instruments on CPU…");
            await Run(Python, ["-c", OfflineSeparationScript, "--device", "cpu", "--name", "htdemucs", "--float32", "--shifts", "1", "-j", "1", "--out", output, input], ct);
            var stems = new[] { "drums", "bass", "other", "vocals" }.Select(name =>
                new DjStemInput(Path.Combine(output, "htdemucs", "input", name + ".wav"), name)).ToArray();
            if (stems.Any(s => !File.Exists(s.Path))) throw new InvalidDataException("Stem separation did not produce all four stems.");
            return new DjSeparatedStems(root, output, stems);
        }
        catch { TempStorageCleanup.DeleteOwnedTree(root, output); throw; }
        finally { Operation.Release(); }
    }

    public async Task RemoveAsync()
    {
        await Operation.WaitAsync();
        try
        {
            if (File.Exists(Marker)) File.Delete(Marker);
            TempStorageCleanup.DeleteOwnedTree(Path.GetDirectoryName(_root)!, _root);
            if (Directory.Exists(_root)) throw new IOException("Some AI files are still in use. Close active separation tasks and try again.");
        }
        finally { Operation.Release(); }
    }
    private static void RequireOptIn(bool optedIn)
    {
        if (!optedIn) throw new InvalidOperationException("Enable local AI stem separation in Settings → Playback first.");
    }
    private const string OfflineGuard = "import torch\ndef no_download(*a, **k): raise RuntimeError('Model missing. Use Install / repair AI tools.')\ntorch.hub.download_url_to_file = no_download\n";
    private const string OfflineSeparationScript = OfflineGuard + "from demucs.separate import main\nmain()";
    public async Task<bool> ValidateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(Python) || !File.Exists(Checkpoint)) return false;
        try
        {
            var result = await _execute(Python, ["-c", OfflineGuard +
                "import hashlib, pathlib, soundfile, torchaudio\np = pathlib.Path(torch.hub.get_dir()) / 'checkpoints' / '955717e8-8726e21a.th'\nassert hashlib.sha256(p.read_bytes()).hexdigest().startswith('8726e21a'), 'Model checksum mismatch'\nfrom demucs.pretrained import get_model\nm = get_model('htdemucs')\nassert set(m.sources) == {'drums','bass','other','vocals'}\nprint('Ready')"], ct);
            return result.Succeeded;
        }
        catch (OperationCanceledException) { throw; }
        catch { return false; }
    }
    private async Task Run(string executable, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var result = await _execute(executable, arguments, ct);
        if (!result.Succeeded) throw new InvalidOperationException(result.TimedOut ? "AI task timed out." : $"AI task failed: {result.Stderr[^Math.Min(1800, result.Stderr.Length)..]}");
    }
}

public sealed class DjSeparatedStems(string root, string output, IReadOnlyList<DjStemInput> stems) : IDisposable
{
    public IReadOnlyList<DjStemInput> Stems => stems;
    public void Dispose() => TempStorageCleanup.DeleteOwnedTree(root, output);
}
