using System.Reflection;
using System.Windows;
using Application = System.Windows.Application;

namespace Chum.App.Views;

public partial class AboutWindow : Window
{
    private string _diagnosticsText = string.Empty;

    public AboutWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        var s = app.Settings.Current;

        var version = Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "dev";
        VersionLabel.Text = $"v{version}";

        ProviderLabel.Text = s.LocalOnlyMode ? "Ollama (local)" : s.LlmProvider;
        ModelLabel.Text = s.LocalOnlyMode ? s.OllamaModel : s.LlmModel;
        WhisperModelLabel.Text = $"Whisper API: {s.CloudSttModel} @ {(string.IsNullOrWhiteSpace(s.CloudSttBaseUrl) ? "OpenAI" : s.CloudSttBaseUrl)}";
        AccelLabel.Text = "CPU (local server)";

        if (app.Orchestrator is { } orch)
        {
            var (segments, sttP50, sttP90, sttP99, llmQueries, llmP50, llmP90, llmP99) = orch.GetLatencyStats();
            SegmentsLabel.Text = segments.ToString("N0");
            if (segments > 0)
            {
                P50Label.Text = FormatMs(sttP50);
                P90Label.Text = FormatMs(sttP90);
                P99Label.Text = FormatMs(sttP99);
            }
            else
            {
                P50Label.Text = P90Label.Text = P99Label.Text = "—  (no data yet)";
            }

            LlmQueriesLabel.Text = llmQueries.ToString("N0");
            if (llmQueries > 0)
            {
                LlmP50Label.Text = FormatMs(llmP50);
                LlmP90Label.Text = FormatMs(llmP90);
                LlmP99Label.Text = FormatMs(llmP99);
            }
            else
            {
                LlmP50Label.Text = LlmP90Label.Text = LlmP99Label.Text = "—  (no queries yet)";
            }

            var (costQueries, inTk, outTk, totalCost) = orch.GetCostStats();
            SessionCostLabel.Text = costQueries > 0
                ? $"${totalCost:F4}  (↑{inTk:N0} ↓{outTk:N0} tokens, {costQueries} queries)"
                : "—  (no queries yet)";
        }
        else
        {
            SegmentsLabel.Text = "—";
            P50Label.Text = P90Label.Text = P99Label.Text = "—";
            LlmQueriesLabel.Text = "—";
            LlmP50Label.Text = LlmP90Label.Text = LlmP99Label.Text = "—";
            SessionCostLabel.Text = "—";
        }

        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Chum", "Logs");
        LogPathLabel.Text = $"Logs: {logDir}";

        _diagnosticsText = BuildDiagnosticsText(version, s, app.Orchestrator, SessionCostLabel.Text);
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(_diagnosticsText);
        CopyDiagBtn.Content = "Copied!";
    }

    private static string FormatMs(double ms) => $"{ms:F0} ms";

    private static string BuildDiagnosticsText(string version, Models.AppSettings s,
        Services.MeetingOrchestrator? orch, string sessionCost)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Chum v{version} — Diagnostics — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"LLM provider : {(s.LocalOnlyMode ? "Ollama (local)" : s.LlmProvider)}");
        sb.AppendLine($"LLM model    : {(s.LocalOnlyMode ? s.OllamaModel : s.LlmModel)}");
        sb.AppendLine($"STT engine   : Whisper API ({s.CloudSttModel})");
        sb.AppendLine($"STT accel    : CPU (local server)");

        if (orch is not null)
        {
            var (segments, sttP50, sttP90, sttP99, llmQ, llmP50, llmP90, llmP99) = orch.GetLatencyStats();
            sb.AppendLine($"Segments     : {segments}");
            sb.AppendLine($"STT p50      : {FormatMs(sttP50)}");
            sb.AppendLine($"STT p90      : {FormatMs(sttP90)}");
            sb.AppendLine($"STT p99      : {FormatMs(sttP99)}");
            sb.AppendLine($"LLM queries  : {llmQ}");
            sb.AppendLine($"LLM p50      : {FormatMs(llmP50)}");
            sb.AppendLine($"LLM p90      : {FormatMs(llmP90)}");
            sb.AppendLine($"LLM p99      : {FormatMs(llmP99)}");
            sb.AppendLine($"Session cost : {sessionCost}");
        }

        sb.AppendLine($"OS           : {Environment.OSVersion}");
        sb.AppendLine($"WorkingSet   : {Environment.WorkingSet / 1_048_576} MB");
        return sb.ToString();
    }
}
