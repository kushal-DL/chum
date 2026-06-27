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
        WhisperModelLabel.Text = s.WhisperModel;

        if (app.Orchestrator is { } orch)
        {
            var (segments, p50, p90, p99) = orch.GetLatencyStats();
            SegmentsLabel.Text = segments.ToString("N0");
            if (segments > 0)
            {
                P50Label.Text = FormatMs(p50);
                P90Label.Text = FormatMs(p90);
                P99Label.Text = FormatMs(p99);
            }
            else
            {
                P50Label.Text = P90Label.Text = P99Label.Text = "—  (no data yet)";
            }
        }
        else
        {
            SegmentsLabel.Text = "—";
            P50Label.Text = P90Label.Text = P99Label.Text = "—";
        }

        var logDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Chum", "Logs");
        LogPathLabel.Text = $"Logs: {logDir}";

        _diagnosticsText = BuildDiagnosticsText(version, s, app.Orchestrator);
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(_diagnosticsText);
        CopyDiagBtn.Content = "Copied!";
    }

    private static string FormatMs(double ms) => $"{ms:F0} ms";

    private static string BuildDiagnosticsText(string version, Models.AppSettings s, Services.MeetingOrchestrator? orch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Chum v{version} — Diagnostics — {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"LLM provider : {(s.LocalOnlyMode ? "Ollama (local)" : s.LlmProvider)}");
        sb.AppendLine($"LLM model    : {(s.LocalOnlyMode ? s.OllamaModel : s.LlmModel)}");
        sb.AppendLine($"Whisper model: {s.WhisperModel}");

        if (orch is not null)
        {
            var (segments, p50ms, p90ms, p99ms) = orch.GetLatencyStats();
            sb.AppendLine($"Segments     : {segments}");
            sb.AppendLine($"STT p50      : {FormatMs(p50ms)}");
            sb.AppendLine($"STT p90      : {FormatMs(p90ms)}");
            sb.AppendLine($"STT p99      : {FormatMs(p99ms)}");
        }

        sb.AppendLine($"OS           : {Environment.OSVersion}");
        sb.AppendLine($"WorkingSet   : {Environment.WorkingSet / 1_048_576} MB");
        return sb.ToString();
    }
}
