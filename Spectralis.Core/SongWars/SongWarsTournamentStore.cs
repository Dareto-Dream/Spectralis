using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.SongWars;

public sealed class SongWarsTournamentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _rootPath;

    public SongWarsTournamentStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "SongWars")) { }

    internal SongWarsTournamentStore(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
    }

    public IReadOnlyList<string> ListTournamentIds()
    {
        if (!Directory.Exists(_rootPath)) return [];
        return Directory.EnumerateDirectories(_rootPath)
            .Select(Path.GetFileName)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task SaveTournamentAsync(SongWarsTournament tournament, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tournament.TournamentId))
            throw new ArgumentException("Song Wars tournament id is required.", nameof(tournament));

        var root = GetTournamentRoot(tournament.TournamentId);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "votes"));

        await WriteJsonAtomicAsync(Path.Combine(root, "tournament.json"), tournament, cancellationToken);
        await WriteJsonAtomicAsync(Path.Combine(root, "judges.json"), tournament.Judges, cancellationToken);
        await WriteAuditAsync(Path.Combine(root, "audit.jsonl"), tournament.AuditLog, cancellationToken);
    }

    public async Task<SongWarsTournament?> LoadTournamentAsync(string tournamentId, CancellationToken cancellationToken = default)
    {
        var root = GetTournamentRoot(tournamentId);
        var path = Path.Combine(root, "tournament.json");
        if (!File.Exists(path)) return null;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var tournament = await JsonSerializer.DeserializeAsync<SongWarsTournament>(stream, JsonOptions, cancellationToken);
        if (tournament is null) return null;

        var judgesPath = Path.Combine(root, "judges.json");
        if (File.Exists(judgesPath))
        {
            await using var judgesStream = new FileStream(judgesPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            tournament.Judges = await JsonSerializer.DeserializeAsync<List<SongWarsJudge>>(judgesStream, JsonOptions, cancellationToken) ?? [];
        }

        var auditPath = Path.Combine(root, "audit.jsonl");
        if (File.Exists(auditPath))
            tournament.AuditLog = await ReadAuditAsync(auditPath, cancellationToken);

        return tournament;
    }

    public async Task SaveVotesAsync(string tournamentId, string matchId, IReadOnlyCollection<SongWarsVote> votes, CancellationToken cancellationToken = default)
    {
        var votesRoot = Path.Combine(GetTournamentRoot(tournamentId), "votes");
        Directory.CreateDirectory(votesRoot);
        await WriteJsonAtomicAsync(Path.Combine(votesRoot, $"{SafeFileName(matchId)}.json"), votes, cancellationToken);
    }

    public async Task<List<SongWarsVote>> LoadVotesAsync(string tournamentId, string matchId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetTournamentRoot(tournamentId), "votes", $"{SafeFileName(matchId)}.json");
        if (!File.Exists(path)) return [];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<List<SongWarsVote>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    public void DeleteTournament(string tournamentId)
    {
        var root = GetTournamentRoot(tournamentId);
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private string GetTournamentRoot(string tournamentId)
    {
        var safeId = SafeFileName(tournamentId);
        var path = Path.GetFullPath(Path.Combine(_rootPath, safeId));
        if (!IsUnderRoot(path))
            throw new InvalidOperationException("Song Wars refused to access data outside its store.");
        return path;
    }

    private async Task WriteJsonAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsUnderRoot(fullPath))
            throw new InvalidOperationException("Song Wars refused to write outside its store.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? _rootPath);
        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? _rootPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
            }
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally { TryDelete(tempPath); }
    }

    private async Task WriteAuditAsync(string path, IReadOnlyCollection<SongWarsAuditEntry> entries, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsUnderRoot(fullPath))
            throw new InvalidOperationException("Song Wars refused to write outside its store.");
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? _rootPath);
        var tempPath = Path.Combine(Path.GetDirectoryName(fullPath) ?? _rootPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await using (var writer = new StreamWriter(stream))
            {
                foreach (var entry in entries.OrderBy(e => e.AtUtc))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(JsonSerializer.Serialize(entry, JsonOptions));
                }
            }
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally { TryDelete(tempPath); }
    }

    private static async Task<List<SongWarsAuditEntry>> ReadAuditAsync(string path, CancellationToken cancellationToken)
    {
        var entries = new List<SongWarsAuditEntry>();
        using var reader = new StreamReader(path);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<SongWarsAuditEntry>(line, JsonOptions);
                if (entry is not null) entries.Add(entry);
            }
            catch { }
        }
        return entries;
    }

    private bool IsUnderRoot(string candidatePath)
    {
        var normalizedRoot = _rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(candidatePath).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFileName(string value)
    {
        var safe = new string((value ?? "").Trim()
            .Where(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            .Take(96).ToArray());
        if (string.IsNullOrWhiteSpace(safe))
            throw new ArgumentException("Song Wars id cannot be empty.", nameof(value));
        return safe;
    }

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
