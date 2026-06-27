using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using Serilog;
using Timer = System.Threading.Timer;

namespace Chum.App.Services;

/// <summary>
/// Polls the Teams window via Windows UI Automation at 500ms intervals and extracts
/// live caption text. Fires CaptionLineReceived whenever new non-duplicate text is found.
///
/// Teams AutomationIds change between versions. We try a layered search strategy:
///   1. Elements with AutomationId containing "caption" (case-insensitive)
///   2. Elements with ClassName containing "caption"
///   3. Elements named "Captions" in the accessibility tree
///
/// Returns null (silently) if Teams is not running or captions are not active.
/// COM exceptions are caught per-poll; never crashes the poll loop.
/// </summary>
public sealed class TeamsCaptionsReader : IDisposable
{
    private Timer? _timer;
    private string? _lastText;
    private bool _disposed;

    /// <summary>Fires on background thread when new caption text is detected.</summary>
    public event EventHandler<string>? CaptionLineReceived;

    /// <summary>True while the reader is actively polling.</summary>
    public bool IsPolling { get; private set; }

    public void Start()
    {
        if (IsPolling) return;
        IsPolling = true;
        _lastText = null;
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(500));
        Log.Information("TeamsCaptionsReader: polling started");
    }

    public void Stop()
    {
        if (!IsPolling) return;
        IsPolling = false;
        _timer?.Dispose();
        _timer = null;
        _lastText = null;
        Log.Information("TeamsCaptionsReader: polling stopped");
    }

    private void Poll()
    {
        try
        {
            var text = TryReadCaptionsText();
            if (text is null || text == _lastText) return;
            _lastText = text;
            CaptionLineReceived?.Invoke(this, text);
        }
        catch (ElementNotAvailableException) { /* Teams window closed or minimised */ }
        catch (Exception ex)
        {
            Log.Verbose(ex, "TeamsCaptionsReader: poll error");
        }
    }

    private static string? TryReadCaptionsText()
    {
        // Find any running Teams process with a visible main window
        var teamsProcess = Process.GetProcessesByName("ms-teams")
            .Concat(Process.GetProcessesByName("Teams"))
            .Concat(Process.GetProcessesByName("teams2"))
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (teamsProcess is null) return null;

        AutomationElement? root;
        try { root = AutomationElement.FromHandle(teamsProcess.MainWindowHandle); }
        catch { return null; }
        if (root is null) return null;

        // Strategy 1: look for elements whose AutomationId contains "caption" (new Teams: varies per build)
        var text = FindTextByPropertyContains(root, AutomationElement.AutomationIdProperty, "caption");
        if (text is not null) return text;

        // Strategy 2: elements whose ClassName contains "caption"
        text = FindTextByPropertyContains(root, AutomationElement.ClassNameProperty, "caption");
        if (text is not null) return text;

        // Strategy 3: elements whose Name is exactly "Captions" (a common accessibility label)
        text = FindTextByName(root, "Captions");
        return text;
    }

    /// <summary>
    /// Searches descendants for elements whose given property value contains the search string
    /// (case-insensitive). Aggregates non-empty Name and Value text from all matching elements.
    /// </summary>
    private static string? FindTextByPropertyContains(
        AutomationElement root, AutomationProperty property, string contains)
    {
        try
        {
            // UI Automation doesn't support "contains" conditions natively — walk the full tree.
            // To avoid scanning the entire Teams element tree (which can be very large) we use
            // a depth-limited walk: only descend if the parent Name/Class hint looks relevant.
            var walker = TreeWalker.ContentViewWalker;
            var sb = new StringBuilder();
            WalkForCaption(walker, root, property, contains, sb, depth: 0, maxDepth: 8);
            return sb.Length > 0 ? sb.ToString().Trim() : null;
        }
        catch { return null; }
    }

    private static void WalkForCaption(TreeWalker walker, AutomationElement el,
        AutomationProperty prop, string search, StringBuilder result, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;
        try
        {
            var propVal = el.GetCurrentPropertyValue(prop)?.ToString() ?? string.Empty;
            if (propVal.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                // Collect text from this element and its immediate children
                var name = el.Current.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    result.Append(name).Append(' ');

                // Try ValuePattern for editable text fields
                if (el.TryGetCurrentPattern(ValuePattern.Pattern, out var vp))
                {
                    var val = ((ValuePattern)vp).Current.Value;
                    if (!string.IsNullOrWhiteSpace(val))
                        result.Append(val).Append(' ');
                }
            }

            // Recurse into children
            var child = walker.GetFirstChild(el);
            while (child is not null)
            {
                WalkForCaption(walker, child, prop, search, result, depth + 1, maxDepth);
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException) { }
        catch (InvalidOperationException) { }
    }

    private static string? FindTextByName(AutomationElement root, string name)
    {
        try
        {
            var cond = new PropertyCondition(AutomationElement.NameProperty, name);
            var el = root.FindFirst(TreeScope.Descendants, cond);
            if (el is null) return null;
            return el.Current.Name.Length > 0 ? el.Current.Name : null;
        }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
