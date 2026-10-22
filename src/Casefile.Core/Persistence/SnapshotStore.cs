using System.Text.Json;
using System.Text.Json.Serialization;
using Casefile.Core.Domain;

namespace Casefile.Core.Persistence;

public sealed record Snapshot(int SchemaVersion, DateTimeOffset SavedAt, IReadOnlyList<Case> Cases);

/// <summary>
/// JSON snapshot persistence. The index and link graph are derived data and are never stored: they are
/// rebuilt from the cases on load, so the file format only has to stay compatible for the domain model.
/// Writes go to a temp file first and are then moved over the target, so a crash mid-write never leaves
/// a truncated snapshot behind.
/// </summary>
public sealed class SnapshotStore(string path)
{
    public const int CurrentSchemaVersion = 1;

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Path { get; } = path;

    public bool Exists => File.Exists(Path);

    public async Task SaveAsync(IReadOnlyList<Case> cases, CancellationToken ct = default)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = Path + ".tmp";
        var snapshot = new Snapshot(CurrentSchemaVersion, DateTimeOffset.UtcNow, cases);
        await using (var stream = File.Create(tmp))
            await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, ct);
        File.Move(tmp, Path, overwrite: true);
    }

    public async Task<IReadOnlyList<Case>> LoadAsync(CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(Path);
        var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, JsonOptions, ct)
                       ?? throw new InvalidDataException("Empty snapshot.");
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported snapshot schema version {snapshot.SchemaVersion}.");
        return snapshot.Cases;
    }
}
