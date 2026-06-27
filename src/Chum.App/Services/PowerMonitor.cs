using System.Windows.Forms;
using Serilog;
using Timer = System.Threading.Timer;

namespace Chum.App.Services;

/// <summary>
/// Monitors AC/battery power state and fires an event when it changes.
/// Polls every 30 s — sufficient for battery-state detection; no need for
/// Windows power notification messages at this fidelity.
/// </summary>
public sealed class PowerMonitor : IDisposable
{
    /// <summary>Fired on the thread-pool when power state changes.</summary>
    public event EventHandler<bool>? OnBatteryChanged;

    public bool IsOnBattery { get; private set; }

    private Timer? _timer;
    private bool _disposed;

    public void Start()
    {
        IsOnBattery = DetectBattery();
        _timer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        Log.Information("PowerMonitor started — OnBattery={OnBattery}", IsOnBattery);
    }

    private void Poll()
    {
        try
        {
            var onBattery = DetectBattery();
            if (onBattery == IsOnBattery) return;
            IsOnBattery = onBattery;
            Log.Information("Power state changed — OnBattery={OnBattery}", onBattery);
            OnBatteryChanged?.Invoke(this, onBattery);
        }
        catch { /* never crash the poll loop */ }
    }

    private static bool DetectBattery()
    {
        var status = SystemInformation.PowerStatus;
        return status.PowerLineStatus == PowerLineStatus.Offline;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer?.Dispose();
    }
}
