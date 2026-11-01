using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chum.Transcription.Models;
using Serilog;

namespace Chum.App.Services;

/// <summary>
/// Streams a meeting session's transcript segments and LLM events to disk.
/// Produces three artefacts per session in %LocalAppData%\Chum\sessions\&lt;ts&gt;\:
///   transcript.jsonl — one line per Whisper segment, written as segments arrive
///   events.jsonl     — Q&amp;A pairs and vision events, written as they complete
///   images\          — JPEG screenshots referenced by vision events (max 200)
/// None of these methods block the overlay or the transcription pipeline.
/// </summary>
public sealed class SessionJournal : IDisposable
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private static string SessionsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Chum", "sessions");

    private readonly string _sessionDir;
    private readonly Lock _writeLock = new();
    private StreamWriter? _transcriptWriter;
    private StreamWriter? _eventsWriter;
    private int _imageCount;
    private bool _started;
    private bool _disposed;

    private const int MaxImages = 200;

    public string SessionFolder => _sessionDir;
    public bool IsStarted => _started;

    public SessionJournal()
    {
        var ts = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
        _sessionDir = Path.Combine(SessionsRoot, ts);
    }

    public void Start()
    {
        if (_started || _disposed) return;
        try
        {
            Directory.CreateDirectory(_sessionDir);
            Directory.CreateDirectory(Path.Combine(_sessionDir, "images"));
            _transcriptWriter = new StreamWriter(
                Path.Combine(_sessionDir, "transcript.jsonl"),
                append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
            _eventsWriter = new StreamWriter(
                Path.Combine(_sessionDir, "events.jsonl"),
                append: false, System.Text.Encoding.UTF8) { AutoFlush = true };
            _started = true;
            Log.Information("Session journal started: {Dir}", _sessionDir);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Session journal could not start — disk logging disabled");
        }
    }

    public void Stop()
    {
        lock (_writeLock)
        {
            if (!_started) return;
            _started = false;
            try { _transcriptWriter?.Flush(); _transcriptWriter?.Dispose(); } catch { }
            try { _eventsWriter?.Flush();     _eventsWriter?.Dispose(); }     catch { }
            _transcriptWriter = null;
            _eventsWriter = null;
            Log.Information("Session journal stopped: {Dir}", _sessionDir);
        }
    }

    public void WriteSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0) return;
        lock (_writeLock)
        {
            if (!_started || _transcriptWriter is null) return;
            foreach (var seg in segments)
            {
                var entry = new TranscriptEntry(
                    Ts:      seg.Timestamp.ToString("O"),
                    Type:    "transcript",
                    Speaker: seg.SpeakerLabel,
                    Text:    seg.Text,
                    Source:  seg.Source.ToString(),
                    Conf:    seg.Confidence);
                _transcriptWriter.WriteLine(JsonSerializer.Serialize(entry, _json));
            }
        }
    }

    /// <summary>Returns relative path "images/&lt;ts&gt;.jpg" or null when limit is reached.</summary>
    public string? SaveImage(string imageBase64)
    {
        lock (_writeLock)
        {
            if (!_started) return null;
            if (_imageCount >= MaxImages)
            {
                if (_imageCount == MaxImages)
                    Log.Warning("Session image limit ({Max}) reached — further screenshots not saved to disk", MaxImages);
                _imageCount++;
                return null;
            }
            try
            {
                var filename = $"{DateTimeOffset.Now:yyyyMMdd_HHmmss_fff}.jpg";
                var fullPath = Path.Combine(_sessionDir, "images", filename);
                File.WriteAllBytes(fullPath, Convert.FromBase64String(imageBase64));
                _imageCount++;
                return $"images/{filename}";
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to save session image to disk");
                return null;
            }
        }
    }

    public void WriteQaEvent(string question, string answer, string queryType)
    {
        lock (_writeLock)
        {
            if (!_started || _eventsWriter is null) return;
            var entry = new QaEntry(
                Ts:       DateTimeOffset.Now.ToString("O"),
                Type:     "qa",
                QType:    queryType,
                Question: question,
                Answer:   answer);
            _eventsWriter.WriteLine(JsonSerializer.Serialize(entry, _json));
        }
    }

    public void WriteVisionEvent(string? imagePath, string caption, string llmResponse, string queryType)
    {
        lock (_writeLock)
        {
            if (!_started || _eventsWriter is null) return;
            var entry = new VisionEntry(
                Ts:        DateTimeOffset.Now.ToString("O"),
                Type:      "vision",
                QType:     queryType,
                ImagePath: imagePath,
                Caption:   caption,
                Response:  llmResponse);
            _eventsWriter.WriteLine(JsonSerializer.Serialize(entry, _json));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── JSONL entry shapes ──────────────────────────────────────────────────

    private sealed record TranscriptEntry(
        [property: JsonPropertyName("ts")]     string Ts,
        [property: JsonPropertyName("type")]   string Type,
        [property: JsonPropertyName("speaker")] string Speaker,
        [property: JsonPropertyName("text")]   string Text,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("conf")]   float Conf);

    private sealed record QaEntry(
        [property: JsonPropertyName("ts")]       string Ts,
        [property: JsonPropertyName("type")]     string Type,
        [property: JsonPropertyName("q_type")]   string QType,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("answer")]   string Answer);

    private sealed record VisionEntry(
        [property: JsonPropertyName("ts")]         string Ts,
        [property: JsonPropertyName("type")]       string Type,
        [property: JsonPropertyName("q_type")]     string QType,
        [property: JsonPropertyName("image_path")] string? ImagePath,
        [property: JsonPropertyName("caption")]    string Caption,
        [property: JsonPropertyName("response")]   string Response);
}
