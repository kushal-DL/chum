using System.IO;
using System.Text;
using System.Text.Json;
using Serilog;
using UglyToad.PdfPig; // PdfPig NuGet package

namespace Chum.App.Services;

public sealed record DocumentEntry(string Name, string Content);

/// <summary>
/// Manages user-uploaded documents (PDF, TXT, MD, etc.) that are either injected
/// directly into LLM prompts or indexed by the ASP RAG bridge for semantic retrieval.
/// Stored in %APPDATA%\Chum\documents.json.
/// </summary>
public sealed class DocumentContextService
{
    private static string StorePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Chum", "documents.json");

    private const int MaxDocuments = 1000;
    private const int MaxFallbackChars = 15_000;
    private const int ChunkSize = 1500;
    private const int ChunkOverlap = 200;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Documents & Markup
        ".pdf", ".txt", ".md", ".markdown", ".rst", ".log",
        // Web & Frontend
        ".html", ".htm", ".css", ".js", ".jsx", ".ts", ".tsx",
        // Python & Data
        ".py", ".pyi", ".json", ".csv", ".yaml", ".yml", ".toml", ".xml", ".ini",
        // Systems & Backend Code
        ".cs", ".xaml", ".c", ".cpp", ".cc", ".cxx", ".h", ".hpp",
        ".rs", ".go", ".java", ".kt", ".sql", ".graphql",
        // Scripts & DevOps
        ".sh", ".bash", ".ps1", ".bat", ".cmd", ".dockerfile"
    };

    private readonly List<DocumentEntry> _docs = new();

    public IReadOnlyList<DocumentEntry> Documents => _docs;

    public event EventHandler? DocumentsChanged;

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

    public string? AddDocument(string filePath, bool chunkLongDocs = true)
    {
        if (_docs.Count >= MaxDocuments)
            return $"Maximum {MaxDocuments} document chunks reached — please remove some first.";

        var ext = Path.GetExtension(filePath);
        if (!SupportedExtensions.Contains(ext))
            return $"Unsupported file extension '{ext}'. Supported: {string.Join(", ", SupportedExtensions)}";

        string content;
        try
        {
            content = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) ? ExtractPdf(filePath) : File.ReadAllText(filePath);
        }
        catch (Exception ex)
        {
            return $"Could not read file: {ex.Message}";
        }

        content = content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return "File is empty.";

        var fileName = Path.GetFileName(filePath);

        if (chunkLongDocs && content.Length > ChunkSize)
        {
            var chunks = ChunkText(content, ChunkSize, ChunkOverlap);
            int available = MaxDocuments - _docs.Count;
            int countToAdd = Math.Min(chunks.Count, available);

            for (int i = 0; i < countToAdd; i++)
            {
                var title = chunks.Count > 1 ? $"{fileName} [{i + 1}/{chunks.Count}]" : fileName;
                _docs.Add(new DocumentEntry(title, chunks[i]));
            }
        }
        else
        {
            // Single doc entry
            if (content.Length > MaxFallbackChars / 2)
                content = content[..(MaxFallbackChars / 2)] + "\n[...truncated]";

            _docs.Add(new DocumentEntry(fileName, content));
        }

        Save();
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        return null; // null = success
    }

    /// <summary>
    /// Recursively scans a directory for all supported documents, extracts text,
    /// chunks long files, and adds them into the RAG document set.
    /// </summary>
    public (int added, List<string> errors) AddDirectory(string dirPath, bool recursive = true)
    {
        var errors = new List<string>();
        if (!Directory.Exists(dirPath))
        {
            errors.Add($"Directory does not exist: {dirPath}");
            return (0, errors);
        }

        var opt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        int addedCount = 0;

        try
        {
            var files = Directory.EnumerateFiles(dirPath, "*.*", opt)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            foreach (var file in files)
            {
                if (_docs.Count >= MaxDocuments)
                {
                    errors.Add($"Reached maximum document limit of {MaxDocuments}.");
                    break;
                }

                int prevCount = _docs.Count;
                var err = AddDocument(file, chunkLongDocs: true);
                if (err is not null)
                {
                    errors.Add($"{Path.GetFileName(file)}: {err}");
                }
                else
                {
                    addedCount += (_docs.Count - prevCount);
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error scanning directory: {ex.Message}");
        }

        if (addedCount > 0)
        {
            Save();
            DocumentsChanged?.Invoke(this, EventArgs.Empty);
        }

        return (addedCount, errors);
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _docs.Count) return;
        _docs.RemoveAt(index);
        Save();
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAll()
    {
        _docs.Clear();
        Save();
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns a formatted context block to inject into LLM system prompts
    /// as a fallback when ASP RAG is not enabled.
    /// </summary>
    public string? BuildContextBlock()
    {
        if (_docs.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine("--- REFERENCE DOCUMENTS ---");
        int remaining = MaxFallbackChars;
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

    private static List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        var chunks = new List<string>();
        int start = 0;
        while (start < text.Length)
        {
            int length = Math.Min(chunkSize, text.Length - start);
            var chunk = text.Substring(start, length);

            if (start + length < text.Length)
            {
                int lastBreak = Math.Max(chunk.LastIndexOf('\n'), Math.Max(chunk.LastIndexOf(". "), chunk.LastIndexOf(' ')));
                if (lastBreak > chunkSize / 2)
                {
                    chunk = chunk[..(lastBreak + 1)];
                    length = chunk.Length;
                }
            }

            var trimmed = chunk.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                chunks.Add(trimmed);
            }

            start += length - overlap;
            if (start >= text.Length || length <= overlap) break;
        }
        return chunks;
    }
}
