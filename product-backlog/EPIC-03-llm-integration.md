# EPIC-03: LLM Integration & Response Engine

## Stories at a Glance

| Story ID | Title | Priority | Status | SP |
|----------|-------|----------|--------|----|
| US-03-01 | Anthropic Claude API Integration | P0 — MVP | 🔵 Built | 5 |
| US-03-02 | OpenAI API Integration | P1 — High | 🔵 Built | 3 |
| US-03-03 | Local LLM via Ollama | P2 — Medium | 🔵 Built | 5 |
| US-03-04 | Meeting-Optimised System Prompt | P0 — MVP | 🔵 Built | 5 |
| US-03-05 | Streaming Response Display | P0 — MVP | 🔵 Built | 3 |
| US-03-06 | Response History | P1 — High | 🔵 Built | 3 |
| US-03-07 | Cost Estimation & Token Tracking | P2 — Medium | 🔵 Built | 3 |
| US-03-08 | Prompt Templates Library | P2 — Medium | 🔴 Yet to Start | 3 |

**Priority Key:** P0 = MVP Blocker · P1 = High · P2 = Medium · P3 = Low  
**Status Key:** 🔴 Yet to Start · 🟡 Scaffolded · 🔵 Built · ✅ Done (Built & Tested)

---

## Overview

The LLM engine is Chum's brain. When the user presses the hotkey, it:
1. Receives formatted transcript context from EPIC-02
2. Constructs a prompt combining system context + meeting transcript + (optionally) a captured image
3. Calls the configured LLM provider
4. Streams the response token-by-token to the overlay UI

The engine must be fast (first token in <2s), cost-aware, and produce responses calibrated for a meeting assistant (concise, direct, actionable).

---

## Technical Background

### Provider Options

| Provider | Model | Context | Strength | Cost |
|----------|-------|---------|----------|------|
| Anthropic | Claude Sonnet 4.6 | 200K tokens | Long context, nuanced | ~$3/$15 per 1M in/out |
| Anthropic | Claude Haiku 4.5 | 200K tokens | Fast, cheap | ~$0.80/$4 per 1M in/out |
| OpenAI | GPT-4o | 128K tokens | Multimodal, widely known | ~$2.5/$10 per 1M in/out |
| OpenAI | GPT-4o-mini | 128K tokens | Fast, cheap | ~$0.15/$0.60 per 1M in/out |
| Local | Ollama + Llama 3.1 8B | 8K–128K tokens | Free, private, slower | No cost |

**Recommendation for Chum:**
- **Default**: Claude Haiku 4.5 (fast, cheap, good enough for meeting Q&A)
- **Quality mode**: Claude Sonnet 4.6 (better reasoning for complex technical discussions)
- **Vision queries**: Claude Sonnet 4.6 or GPT-4o (both support images)
- **Privacy mode**: Ollama + Llama 3.1 (local, no data leaves machine)

### Streaming
Both Anthropic and OpenAI SDKs support Server-Sent Events (SSE) streaming. Use streaming for all queries — it enables the overlay to show words appearing in real-time, which feels much faster than waiting for the full response.

---

## Stories

### US-03-01: Anthropic Claude API Integration
**Story Points: 5**

**As a** user pressing the hotkey,  
**I want** the app to send my meeting context to Claude and stream the response,  
**so that** I get an intelligent answer within seconds.

**Acceptance Criteria:**
- [ ] Anthropic SDK (`Anthropic.SDK` NuGet) integrated
- [ ] API key loaded from Windows Credential Manager (never from config file)
- [ ] Request sent with correct model, max_tokens, and temperature
- [ ] Response streamed token-by-token; overlay updates in real-time
- [ ] Request includes: system prompt + transcript context + optional image
- [ ] Network timeout: 30s for first token; 120s total
- [ ] API errors (rate limit, auth failure) surfaced as user-readable messages in overlay

**Technical Implementation:**
```csharp
var client = new AnthropicClient(apiKey);
var request = new MessageRequest {
    Model = "claude-haiku-4-5-20251001",
    MaxTokens = 1024,
    System = BuildSystemPrompt(),
    Messages = new[] {
        new Message { Role = "user", Content = BuildUserMessage(transcript, imageBase64) }
    }
};

await foreach (var chunk in client.Messages.StreamMessageAsync(request)) {
    overlayViewModel.AppendResponseText(chunk.Delta?.Text ?? "");
}
```

**Rate Limit Handling:**
- Catch `429 TooManyRequests`; show "Rate limited — retrying in Xs" in overlay
- Exponential backoff: 1s, 2s, 4s (max 3 retries)

---

### US-03-02: OpenAI API Integration
**Story Points: 3**

**As a** user who prefers GPT-4o,  
**I want** to configure Chum to use OpenAI models,  
**so that** I can use my existing OpenAI subscription.

**Acceptance Criteria:**
- [ ] OpenAI SDK (`OpenAI` NuGet) integrated
- [ ] OpenAI API key stored in Windows Credential Manager
- [ ] Same streaming interface as Claude integration
- [ ] Model selectable: gpt-4o, gpt-4o-mini
- [ ] Vision requests routed correctly (image in `content` array with `type: image_url`)

**Technical Notes:**
- Abstract provider behind `ILlmProvider` interface — swapping Claude for GPT changes one config value
- Both providers share the same `IAsyncEnumerable<string>` streaming contract

```csharp
interface ILlmProvider {
    IAsyncEnumerable<string> StreamResponseAsync(LlmRequest request, CancellationToken ct);
}
```

---

### US-03-03: Local LLM via Ollama
**Story Points: 5**

**As a** user in a high-security environment,  
**I want** to run the LLM entirely locally via Ollama,  
**so that** meeting content never leaves my machine.

**Acceptance Criteria:**
- [ ] Chum detects if Ollama is running locally (check `http://localhost:11434`)
- [ ] User can select Ollama as provider in settings; specify model name (e.g., `llama3.1:8b`)
- [ ] Requests sent to Ollama REST API (`/api/chat` with `stream: true`)
- [ ] Streaming response handled via SSE from Ollama
- [ ] If Ollama is not running, clear error: "Ollama not found. Start Ollama or switch to a cloud provider."
- [ ] Vision models supported if Ollama model supports them (e.g., `llava`)

**Technical Notes:**
- Ollama REST API is OpenAI-compatible — can reuse `OpenAiProvider` with base URL override
- Performance: Llama 3.1 8B on a GPU generates ~50 tokens/sec; adequate for meeting Q&A
- Recommend in documentation: use a quantized model (Q4_K_M) for balance of speed/quality

---

### US-03-04: Meeting-Optimised System Prompt
**Story Points: 5**

**As a** user getting AI help during a meeting,  
**I want** the AI responses to be concise, direct, and formatted for quick reading,  
**so that** I can absorb the answer at a glance without disrupting the meeting flow.

**Acceptance Criteria:**
- [ ] Default system prompt instructs the LLM to: be brief (≤150 words default), use bullet points, avoid preamble, answer the question asked
- [ ] System prompt includes meeting context: date, user's name (configurable), stated role
- [ ] System prompt instructs LLM to focus on the last question asked in the transcript
- [ ] User can customise system prompt in settings (advanced option)
- [ ] Response format options: brief (≤150w), standard (≤400w), detailed (no limit)

**Default System Prompt:**
```
You are Chum, a real-time AI assistant for professional meetings.

Context: Today is {date}. The user's name is {user_name}. They are attending a meeting.

You will receive a rolling transcript of the meeting. Your job is to answer the 
question that was most recently asked — either by the user or by a meeting participant.

Rules:
- Be concise: default response ≤150 words
- Use bullet points for multi-part answers
- Skip preamble ("Great question!", "Certainly!", etc.)
- If you are uncertain, say so briefly
- Do not summarise the transcript back unless asked
- Technical accuracy over politeness

The user is in a live meeting and needs the answer NOW.
```

---

### US-03-05: Streaming Response Display
**Story Points: 3**

**As a** user waiting for the LLM response,  
**I want** to see words appear token-by-token in the overlay,  
**so that** the experience feels fast even before the full response is available.

**Acceptance Criteria:**
- [ ] First token appears in overlay within 2s of hotkey release (p50 target)
- [ ] Subsequent tokens appear with ≤100ms latency per chunk
- [ ] Overlay text updates smoothly without flickering
- [ ] Markdown rendered in overlay: `**bold**`, `- bullets`, `code` blocks
- [ ] If response is cut off (network error mid-stream), overlay shows "[Response interrupted]"

**Technical Notes:**
- Use `IAsyncEnumerable<string>` from provider; `await foreach` on UI thread via `Dispatcher.InvokeAsync`
- Markdown rendering: `Markdig` NuGet for parsing; custom WPF FlowDocument renderer for display
- Avoid re-rendering entire document on each token — append to `FlowDocument` incrementally

---

### US-03-06: Response History
**Story Points: 3**

**As a** user reviewing what the AI said earlier in the meeting,  
**I want** to scroll through previous responses in the overlay,  
**so that** I can refer back to an earlier answer without re-querying.

**Acceptance Criteria:**
- [ ] Overlay maintains last 10 responses with their queries
- [ ] User can scroll through history with arrow keys or mouse wheel
- [ ] Each history entry shows: query trigger time, question (inferred from transcript), response
- [ ] "Copy to clipboard" button on each response
- [ ] History cleared when app is restarted (in-session only, by default)

**Technical Notes:**
- `ObservableCollection<ResponseEntry>` bound to overlay `ListBox`
- `ResponseEntry`: `{ DateTimeOffset Timestamp, string InferredQuestion, string Response }`
- Infer question: take last "?" in transcript before hotkey press timestamp

---

### US-03-07: Cost Estimation & Token Tracking
**Story Points: 3**

**As a** user watching my API spend,  
**I want** to see estimated cost per query and cumulative session cost,  
**so that** I understand my spending and can choose a cheaper model if needed.

**Acceptance Criteria:**
- [ ] Token count displayed for each request: input tokens, output tokens
- [ ] Estimated cost shown per response (based on configured model's known pricing)
- [ ] Session total shown in settings panel
- [ ] Warning when session cost exceeds configurable threshold (default: $1.00)
- [ ] Pricing table editable in settings (in case prices change)

**Technical Notes:**
- Input token estimation: use `TiktokenSharp` for GPT; Anthropic SDK returns token counts in response
- Pricing hardcoded with last-known values; user can override in settings JSON
- Session cost stored in memory only; not persisted across restarts

---

### US-03-08: Prompt Templates Library
**Story Points: 3**

**As a** user with specific recurring needs,  
**I want** to save custom prompt templates (e.g., "explain this technically", "draft a follow-up email"),  
**so that** I can quickly switch Chum's behaviour for different meeting types.

**Acceptance Criteria:**
- [ ] User can create, name, and save prompt templates
- [ ] Templates accessible via hotkey modifier (e.g., Ctrl+Alt+1 through Ctrl+Alt+5)
- [ ] Templates include: system prompt override, response length target, optional fixed prefix
- [ ] Built-in templates: "Quick Answer", "Detailed Explanation", "Action Items", "Devil's Advocate"
- [ ] Templates stored in `%APPDATA%\Chum\templates.json`

**Built-in Template Examples:**
| Template | Behaviour |
|----------|-----------|
| Quick Answer | ≤50 words, direct, no bullets |
| Detailed Explanation | Full explanation with context, up to 500 words |
| Action Items | Extract action items from recent transcript; ignore question trigger |
| Devil's Advocate | Counter the last stated position with key objections |
| Draft Reply | Draft a verbal response the user can say aloud |

---

## Epic-Level Challenge Matrix

| Challenge | Severity | Mitigation |
|-----------|----------|------------|
| API key management (accidental exposure) | High | Windows Credential Manager; never write to config files; mask in logs |
| Rate limiting during busy meetings (many queries) | Medium | Exponential backoff; deduplicate consecutive identical queries; per-minute request budget |
| Long transcript exceeds context window | High | Summarisation strategy from EPIC-02; sliding window; configurable token budget |
| Network latency on corporate VPN | Medium | Show "waiting for response..." immediately; stream to overlay ASAP; set generous timeout |
| LLM response includes outdated technical info | Low | System prompt: "If asked about current events, say you may not have the latest info" |
| Ollama cold start (model load ~10s) | Medium | Detect on startup; if Ollama mode selected, warm up model with a dummy request |

---

## Dependencies

- EPIC-02 (Transcription) provides the formatted context string
- EPIC-04 (Hotkeys) triggers the LLM query
- EPIC-05 (Overlay UI) displays streamed responses
- EPIC-06 (Screen Capture) provides image data for vision queries
- EPIC-07 (Settings) provides API keys, model selection, system prompt customisation
- EPIC-08 (Privacy) may restrict which providers are allowed
