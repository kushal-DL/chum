using NAudio.CoreAudioApi;
using Serilog;

namespace Chum.Audio.Capture;

/// <summary>
/// Finds which WASAPI render endpoint a given set of processes is using for audio output.
/// Used by Teams/Zoom audio device detection (US-09-02, US-09-03).
/// COM exceptions from any individual device are swallowed and logged at Verbose level
/// so one inaccessible device never aborts the full enumeration.
/// </summary>
public static class AudioSessionHelper
{
    /// <summary>
    /// Searches all active WASAPI render endpoints for sessions owned by any PID in <paramref name="pids"/>.
    /// Returns the device ID and friendly name on the first match; false if no match or COM access fails.
    /// Safe to call from background threads.
    /// </summary>
    public static bool TryFindProcessRenderDevice(
        IReadOnlySet<int> pids,
        out string? deviceId,
        out string? deviceFriendlyName)
    {
        deviceId = null;
        deviceFriendlyName = null;
        if (pids.Count == 0) return false;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    try
                    {
                        var sm = device.AudioSessionManager;
                        sm.RefreshSessions();
                        var sessions = sm.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            uint pid;
                            try { pid = sessions[i].GetProcessID; }
                            catch { continue; }

                            if (pids.Contains((int)pid))
                            {
                                deviceId = device.ID;
                                deviceFriendlyName = device.FriendlyName;
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Verbose(ex, "AudioSessionHelper: cannot read sessions on '{Device}'", device.FriendlyName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioSessionHelper: WASAPI render device enumeration failed");
        }

        return false;
    }

    /// <summary>
    /// Searches active WASAPI render endpoints for a device whose FriendlyName contains
    /// <paramref name="namePattern"/>. Returns true with the first matching device ID and name.
    /// Used to detect Zoom's virtual audio device ("Zoom Audio Device").
    /// </summary>
    public static bool TryFindRenderDeviceByName(
        string namePattern,
        out string? deviceId,
        out string? deviceFriendlyName,
        StringComparison comparison = StringComparison.OrdinalIgnoreCase)
    {
        deviceId = null;
        deviceFriendlyName = null;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    if (device.FriendlyName.Contains(namePattern, comparison))
                    {
                        deviceId = device.ID;
                        deviceFriendlyName = device.FriendlyName;
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioSessionHelper: device name search failed for '{Pattern}'", namePattern);
        }
        return false;
    }

    /// <summary>Returns the Windows default WASAPI render device ID (multimedia role), or null on error.</summary>
    public static string? GetDefaultRenderDeviceId()
    {
        try
        {
            using var e = new MMDeviceEnumerator();
            return e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioSessionHelper: could not get default render device ID");
            return null;
        }
    }
}
