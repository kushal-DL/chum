using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace Chum.App.ViewModels;

public enum OverlayStatus { Idle, Initialising, Listening, Thinking, Paused, Error }

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private readonly Dispatcher _dispatcher;

    public OverlayViewModel(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    // ── Response display ──────────────────────────────────────────────────

    private string _responseText = string.Empty;
    public string ResponseText
    {
        get => _responseText;
        private set { _responseText = value; OnPropertyChanged(); }
    }

    private bool _isStreaming;
    public bool IsStreaming
    {
        get => _isStreaming;
        private set { _isStreaming = value; OnPropertyChanged(); }
    }

    public void StartNewResponse()
    {
        Invoke(() =>
        {
            ResponseText = string.Empty;
            IsStreaming = true;
        });
    }

    public void AppendResponseToken(string token)
    {
        Invoke(() => ResponseText += token);
    }

    public void ShowError(string message)
    {
        Invoke(() =>
        {
            ResponseText = $"⚠ {message}";
            IsStreaming = false;
            SetStatus(OverlayStatus.Error, message);
        });
    }

    // ── Transcript strip ──────────────────────────────────────────────────

    private const int MaxTranscriptLines = 5;
    public ObservableCollection<string> TranscriptLines { get; } = [];

    public void AddTranscriptLine(string line)
    {
        Invoke(() =>
        {
            TranscriptLines.Add(line);
            while (TranscriptLines.Count > MaxTranscriptLines)
                TranscriptLines.RemoveAt(0);
        });
    }

    // ── Status ────────────────────────────────────────────────────────────

    private OverlayStatus _currentStatus = OverlayStatus.Idle;
    public OverlayStatus CurrentStatus => _currentStatus;

    private string _statusText = "Idle";
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private string _statusColor = "#888888";
    public string StatusColor
    {
        get => _statusColor;
        private set { _statusColor = value; OnPropertyChanged(); }
    }

    public void SetStatus(OverlayStatus status, string text)
    {
        _currentStatus = status;
        Invoke(() =>
        {
            StatusText = text;
            StatusColor = status switch
            {
                OverlayStatus.Listening => "#22C55E",    // green
                OverlayStatus.Thinking => "#3B82F6",     // blue
                OverlayStatus.Paused => "#EF4444",       // red
                OverlayStatus.Initialising => "#F59E0B", // amber
                OverlayStatus.Error => "#EF4444",
                _ => "#6B7280"                           // grey
            };
            if (!IsStreaming && status != OverlayStatus.Thinking) IsStreaming = false;
        });
    }

    // ── Listening indicator ───────────────────────────────────────────────

    private bool _isListening;
    public bool IsListening
    {
        get => _isListening;
        private set { _isListening = value; OnPropertyChanged(); }
    }

    public void SetListeningState(bool active)
    {
        Invoke(() => IsListening = active);
        if (!active) Invoke(() => IsStreaming = false);
    }

    // ── Visibility ────────────────────────────────────────────────────────

    private Visibility _overlayVisibility = Visibility.Visible;
    public Visibility OverlayVisibility
    {
        get => _overlayVisibility;
        private set { _overlayVisibility = value; OnPropertyChanged(); }
    }

    public void ToggleVisibility()
    {
        Invoke(() => OverlayVisibility =
            OverlayVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible);
    }

    public void Hide() => Invoke(() => OverlayVisibility = Visibility.Collapsed);
    public void Show() => Invoke(() => OverlayVisibility = Visibility.Visible);

    // ── Clipboard notification ────────────────────────────────────────────

    private bool _hasPendingClipboardImage;
    public bool HasPendingClipboardImage
    {
        get => _hasPendingClipboardImage;
        private set { _hasPendingClipboardImage = value; OnPropertyChanged(); }
    }

    public void SetClipboardPending(bool pending)
    {
        Invoke(() => HasPendingClipboardImage = pending);
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.InvokeAsync(action);
    }
}
