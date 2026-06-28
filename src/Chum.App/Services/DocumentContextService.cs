using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;
using UglyToad.PdfPig; // PdfPig NuGet package

namespace Chum.App.Services;

public sealed record DocumentEntry(string Name, string Content);

/// <summary>
/// Manages user-uploaded documents (PDF, TXT, MD) that are injected as extra
/// context into every LLM request. Stored in %APPDATA%\Chum\documents.json.
/// </summary>
public sealed class DocumentContextService
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chum", "documents.json");

    private const int MaxDocuments = 10;
    private const int MaxTotalChars = 15_000;

    private readonly List<DocumentEntry> _docs = new();

    public IReadOnlyList<DocumentEntry> Documents => _docs;

    public void Load()
    {
        if (!File.Exists(StorePath)) return;
        try
        {
            var json = File.ReadAllText(StorePath);
            var list = JsonSerializer.Deserialize<List<DocumentEntry>>(json);
            if (list is not null) { _docs.Clear(); _docs.AddRange(list); }
        }
        catch (Exception ex) { Log.Warning(ex, "Could not load documents.json"); }
    }

    public string? AddDocument(string filePath)
    {
        if (_docs.Count >= MaxDocuments)
            return $"Maximum {MaxDocuments} documents allowed — remove one first.";

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        string content;
        try
        {
            content = ext == ".pdf" ? ExtractPdf(filePath) : File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return $"Could not read file: {ex.Message}";
        }

        // Trim to per-doc budget
        if (content.Length > MaxTotalChars / 2)
            content = content[..(MaxTotalChars / 2)] + "\n[...truncated]";

        _docs.Add(new DocumentEntry(Path.GetFileName(filePath), content));
        Save();
        return null; // null = success
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _docs.Count) return;
        _docs.RemoveAt(index);
        Save();
    }

    /// <summary>
    /// Returns a formatted context block to inject into LLM system prompts.
    /// Returns null when no documents are loaded.
    /// </summary>
    public string? BuildContextBlock()
    {
        if (_docs.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine("--- REFERENCE DOCUMENTS ---");
        int remaining = MaxTotalChars;
        foreach (var doc in _docs)
        {
            if (remaining <= 0) break;
            var excerpt = doc.Content.Length > remaining ? doc.Content[..remaining] + "\n[...truncated]" : doc.Content;
            sb.AppendLine($"[{doc.Name}]");
            sb.AppendLine(excerpt);
            sb.AppendLine();
            remaining -= excerpt.Length;
        }
        sb.AppendLine("--- END REFERENCE DOCUMENTS ---");
        return sb.ToString();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(_docs, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Log.Warning(ex, "Could not save documents.json"); }
    }

    private static string ExtractPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
            sb.AppendLine(page.Text);
        return sb.ToString();
    }
}
