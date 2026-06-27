using NAudio.CoreAudioApi;

namespace Chum.Audio.Capture;

public record AudioDeviceInfo(string Id, string Name, bool IsDefault);

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var e = new MMDeviceEnumerator();
        string? defaultId = TryGetDefaultId(e, DataFlow.Render);
        return [.. e.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))];
    }

    public static IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        using var e = new MMDeviceEnumerator();
        string? defaultId = TryGetDefaultId(e, DataFlow.Capture);
        return [.. e.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .Select(d => new AudioDeviceInfo(d.ID, d.FriendlyName, d.ID == defaultId))];
    }

    private static string? TryGetDefaultId(MMDeviceEnumerator e, DataFlow flow)
    {
        try { return e.GetDefaultAudioEndpoint(flow, Role.Multimedia).ID; }
        catch { return null; }
    }
}
