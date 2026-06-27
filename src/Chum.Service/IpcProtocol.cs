using System.Text.Json.Serialization;

namespace Chum.Service;

// ── Named pipe IPC between ChumHostSvc and Chum.Tray ──────────────────────
// Pipe name: \\.\pipe\ChumIPC
// Protocol: newline-delimited JSON (JSON-Lines). Each message is one UTF-8 line.
// Direction: bidirectional duplex. Service sends TokenStream/StatusUpdate/Heartbeat.
//            Tray sends QueryRequest/CancelRequest/PauseRequest.

public enum IpcMessageType
{
    // Tray → Service
    QueryRequest,
    CancelRequest,
    PauseRequest,
    ResumeRequest,
    // Service → Tray
    TokenStream,
    StreamEnd,
    StatusUpdate,
    Heartbeat,
    AuditEntry,
}

public sealed record IpcMessage(
    [property: JsonPropertyName("type")] IpcMessageType Type,
    [property: JsonPropertyName("payload")] string? Payload = null,
    [property: JsonPropertyName("ts")] long TimestampUtcMs = 0);

public sealed record QueryRequestPayload(
    string ContextText,
    string? ImageBase64,
    string? ImageMediaType);

public sealed record StatusUpdatePayload(
    string Status,       // "Idle" | "Listening" | "Thinking" | "Paused" | "Error"
    string StatusText,
    string StatusColor); // hex e.g. "#22C55E"

public sealed record AuditEntryPayload(
    string Event,        // "QueryFired" | "StreamComplete" | "HotkeyPress" | "ServiceStart" | "ServiceStop"
    string? Provider,
    int? InputTokens,
    int? OutputTokens,
    long? LatencyMs,
    string? HotkeyId);
