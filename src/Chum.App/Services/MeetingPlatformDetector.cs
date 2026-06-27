using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace Chum.App.Services;

public enum MeetingPlatform { Unknown, Teams, GoogleMeet, Zoom, WebEx }

/// <summary>
/// Polls running processes every 5 s to detect which meeting app is active.
/// Fires PlatformChanged when the detected platform changes.
/// Used to enrich the LLM system prompt with meeting context.
/// </summary>
public sealed class MeetingPlatformDetector : IDisposable
{
    public event EventHandler<MeetingPlatform>? PlatformChanged;

    public MeetingPlatform CurrentPlatform { get; private set; } = MeetingPlatform.Unknown;

    private Timer? _timer;
    private bool _disposed;

    public void Start()
    {
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    private void Poll()
    {
        try
        {
            var detected = DetectPlatform();
            if (detected == CurrentPlatform) return;
            CurrentPlatform = detected;
            PlatformChanged?.Invoke(this, detected);
            Serilog.Log.Information("Meeting platform detected: {Platform}", detected);
        }
        catch { /* never crash the poll loop */ }
    }

    private static MeetingPlatform DetectPlatform()
    {
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try { processNames.Add(p.ProcessName); }
            catch { }
        }

        if (processNames.Contains("ms-teams") || processNames.Contains("teams") ||
            processNames.Contains("teams2"))
            return MeetingPlatform.Teams;

        if (processNames.Contains("zoom") || processNames.Contains("zoom.us"))
            return MeetingPlatform.Zoom;

        if (processNames.Contains("CiscoWebexStart") || processNames.Contains("ptoneclk") ||
            processNames.Contains("webex"))
            return MeetingPlatform.WebEx;

        // Google Meet runs in a browser tab — detect by checking if a browser is open.
        // This is imprecise: Chrome/Edge could be open without Meet. Kept as a best-effort hint.
        // A more accurate approach requires UIA or URL inspection (US-06-05).
        if (processNames.Contains("chrome") || processNames.Contains("msedge"))
            return MeetingPlatform.GoogleMeet;

        return MeetingPlatform.Unknown;
    }

    public static string FriendlyName(MeetingPlatform platform) => platform switch
    {
        MeetingPlatform.Teams => "Microsoft Teams",
        MeetingPlatform.GoogleMeet => "Google Meet",
        MeetingPlatform.Zoom => "Zoom",
        MeetingPlatform.WebEx => "Cisco WebEx",
        _ => string.Empty
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
