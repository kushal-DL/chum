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

    private string _liveText = string.Empty;   // accumulates current stream regardless of nav state
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
            if (!string.IsNullOrWhiteSpace(_liveText))
            {
                _history.Add(_liveText);
                while (_history.Count > MaxHistoryItems) _history.RemoveAt(0);
            }
            _liveText = string.Empty;
            _historyIndex = -1;
            ResponseText = string.Empty;
            IsStreaming = true;
            NotifyHistoryChanged();
        });
    }

    public void AppendResponseToken(string token)
    {
        Invoke(() =>
        {
            _liveText += token;
            if (_historyIndex == -1)
                ResponseText += token;
        });
    }

    public void ShowError(string message)
    {
        Invoke(() =>
        {
            _liveText = string.Empty;
            _historyIndex = -1;
            ResponseText = $"⚠ {message}";
            IsStreaming = false;
            SetStatus(OverlayStatus.Error, message);
            NotifyHistoryChanged();
        });
    }

    // ── Response history ──────────────────────────────────────────────────

    private const int MaxHistoryItems = 10;
    private readonly List<string> _history = [];
    private int _historyIndex = -1; // -1 = live; 0..N-1 = viewing past response

    public bool HasHistory => _history.Count > 0;

    public string HistoryLabel => _historyIndex == -1
        ? $"Live  ({_history.Count} saved)"
        : $"{_historyIndex + 1} / {_history.Count}";

    public bool CanGoBack => _history.Count > 0 && (_historyIndex == -1 || _historyIndex > 0);
    public bool CanGoForward => _historyIndex != -1;

    public void NavigateBack()
    {
        Invoke(() =>
        {
            if (_history.Count == 0) return;
            _historyIndex = _historyIndex == -1 ? _history.Count - 1 : _historyIndex - 1;
            ResponseText = _history[_historyIndex];
            NotifyHistoryChanged();
        });
    }

    public void NavigateForward()
    {
        Invoke(() =>
        {
            if (_historyIndex == -1) return;
            _historyIndex++;
            if (_historyIndex >= _history.Count)
            {
                _historyIndex = -1;
                ResponseText = _liveText;
            }
            else
            {
                ResponseText = _history[_historyIndex];
            }
            NotifyHistoryChanged();
        });
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistoryLabel));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanGoForward));
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

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        private set { _isPaused = value; OnPropertyChanged(); }
    }

    public void SetStatus(OverlayStatus status, string text)
    {
        _currentStatus = status;
        Invoke(() =>
        {
            StatusText = text;
            IsPaused = status == OverlayStatus.Paused;
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

    // ── Cost hint ─────────────────────────────────────────────────────────

    private string _lastQueryCostHint = string.Empty;
    public string LastQueryCostHint
    {
        get => _lastQueryCostHint;
        private set { _lastQueryCostHint = value; OnPropertyChanged(); }
    }

    public bool HasCostHint => !string.IsNullOrEmpty(_lastQueryCostHint);

    public void SetLastQueryCost(int inputTokens, int outputTokens, decimal costUsd)
    {
        string hint = costUsd > 0
            ? $"~${costUsd:F4}  ↑{inputTokens:N0} ↓{outputTokens:N0} tk"
            : $"↑{inputTokens:N0} ↓{outputTokens:N0} tk";
        Invoke(() =>
        {
            LastQueryCostHint = hint;
            OnPropertyChanged(nameof(HasCostHint));
        });
    }

    // ── Screen capture confirmation banner ────────────────────────────────

    private bool _hasPendingScreenCapture;
    public bool HasPendingScreenCapture
    {
        get => _hasPendingScreenCapture;
        private set { _hasPendingScreenCapture = value; OnPropertyChanged(); }
    }

    public void SetCapturePending(bool pending)
    {
        Invoke(() => HasPendingScreenCapture = pending);
    }

    // ── Audio level meters ────────────────────────────────────────────────

    private double _loopbackLevelPct;
    public double LoopbackLevelPct
    {
        get => _loopbackLevelPct;
        private set { _loopbackLevelPct = value; OnPropertyChanged(); }
    }

    private double _micLevelPct;
    public double MicLevelPct
    {
        get => _micLevelPct;
        private set { _micLevelPct = value; OnPropertyChanged(); }
    }

    private bool _isLoopbackSpeech;
    public bool IsLoopbackSpeech
    {
        get => _isLoopbackSpeech;
        private set { _isLoopbackSpeech = value; OnPropertyChanged(); }
    }

    private bool _isMicSpeech;
    public bool IsMicSpeech
    {
        get => _isMicSpeech;
        private set { _isMicSpeech = value; OnPropertyChanged(); }
    }

    public void UpdateLoopbackLevel(double pct, bool isSpeech)
        => Invoke(() => { LoopbackLevelPct = pct; IsLoopbackSpeech = isSpeech; });

    public void UpdateMicLevel(double pct, bool isSpeech)
        => Invoke(() => { MicLevelPct = pct; IsMicSpeech = isSpeech; });

    // ── Disclosure reminder banner ────────────────────────────────────────

    private bool _hasDisclosureReminder;
    public bool HasDisclosureReminder
    {
        get => _hasDisclosureReminder;
        private set { if (_hasDisclosureReminder == value) return; _hasDisclosureReminder = value; OnPropertyChanged(); }
    }

    public void ShowDisclosureReminder() => Invoke(() => HasDisclosureReminder = true);
    public void DismissDisclosureReminder() => Invoke(() => HasDisclosureReminder = false);

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
