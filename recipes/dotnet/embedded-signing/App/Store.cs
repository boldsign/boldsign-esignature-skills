using System.Text.Json;

namespace Recipe;

public sealed class Signer
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
}

public sealed class EventRecord
{
    public string? Id { get; set; }
    public string? Type { get; set; }
    public long? Created { get; set; }
}

public sealed class DocumentRecord
{
    public string DocumentId { get; set; } = "";
    public string Title { get; set; } = "";
    public Signer Signer { get; set; } = new();
    public string Status { get; set; } = "Queued";
    public string? Error { get; set; }
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public long? LastEventCreated { get; set; }
    public List<EventRecord> Events { get; set; } = new();
}

/// <summary>A tiny JSON file store keyed by BoldSign document ID. Replace it with your database.</summary>
public sealed class Store(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _lock = new();

    private Dictionary<string, DocumentRecord> Read()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, DocumentRecord>>(File.ReadAllText(path)) ?? new(); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException) { return new(); }
    }

    public List<DocumentRecord> All()
    {
        lock (_lock) return Read().Values.OrderByDescending(d => d.CreatedAt, StringComparer.Ordinal).ToList();
    }

    public DocumentRecord? Get(string documentId)
    {
        lock (_lock) return Read().GetValueOrDefault(documentId);
    }

    public DocumentRecord Save(DocumentRecord record)
    {
        lock (_lock)
        {
            var data = Read();
            data[record.DocumentId] = record;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, JsonSerializer.Serialize(data, Json));
        }
        return record;
    }
}
