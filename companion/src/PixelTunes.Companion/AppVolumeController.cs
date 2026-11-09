using System.Diagnostics;
using System.Runtime.InteropServices;
using PixelTunes.Core;

namespace PixelTunes.Companion;

internal sealed record VolumeResult(bool Ok, int Percent, string AppName, string Message);

internal sealed record MixerEntry(uint ProcessId, string ProcessName, float Volume, string Access);

/// <summary>
/// Changes one app's own slider in the Windows volume mixer (Settings > System > Sound >
/// Volume mixer), using the Windows Core Audio API. The main system volume and every other
/// app, including WoW, are left alone.
///
/// This calls the COM interfaces directly through their function tables instead of using
/// .NET's automatic COM wrappers: the wrappers failed on Windows 11 with "Specified cast is
/// not valid", and doing it by hand also lets every failure name the exact step.
/// All calls run on a dedicated multithreaded-apartment (MTA) thread, as Core Audio expects.
/// </summary>
internal static unsafe class AppVolumeController
{
    public static Task<VolumeResult> AdjustAsync(string mediaAppId, bool up, int stepPercent) =>
        RunOnMtaThread(() => Adjust(mediaAppId, up, stepPercent));

    public static Task<List<MixerEntry>> ListAsync() => RunOnMtaThread(() =>
    {
        var list = new List<MixerEntry>();
        try
        {
            ForEachSession((pid, name, _, volume, _, probe) =>
            {
                float level = -1;
                if (volume is { } v && v.Get(out float l) >= 0) level = l;
                list.Add(new MixerEntry(pid, name, level, (volume is { IsChannel: true } ? "via channels; " : "") + probe));
            });
        }
        catch (Exception ex)
        {
            Log.Warn($"Listing volume mixer failed: {ex.Message}");
        }
        return list;
    });

    private static VolumeResult Adjust(string mediaAppId, bool up, int stepPercent)
    {
        var matcher = AudioAppMatcher.ForMediaApp(mediaAppId);
        try
        {
            float? current = null, next = null;
            int matched = 0;
            var failures = new List<string>();
            bool viaChannels = false;
            ForEachSession((pid, name, instanceId, maybeVolume, failure, probe) =>
            {
                if (!matcher.Matches(name, instanceId)) return;
                if (maybeVolume is not { } volume)
                {
                    failures.Add(failure ?? $"no volume control for {name}");
                    Log.Warn($"{name} (pid {pid}) volume access: {probe}");
                    return;
                }
                if (volume.IsChannel && !viaChannels)
                {
                    viaChannels = true;
                    Log.Info($"{name}: master slider refused, using channel volumes ({probe})");
                }
                matched++;
                // Spotify can have several audio sessions; move them all to the same new level.
                if (current == null)
                {
                    Check(volume.Get(out float level), $"reading {name}'s volume");
                    current = level;
                    next = VolumeMath.Step(level, up, stepPercent);
                }
                Check(volume.Set(next!.Value), $"setting {name}'s volume");
            }, failures);

            if (matched == 0 && failures.Count > 0)
            {
                Log.Warn($"Volume change failed for '{mediaAppId}': {string.Join(" | ", failures.Distinct())}");
                return new VolumeResult(false, -1, matcher.DisplayName, failures[0]);
            }
            if (matched == 0)
                return new VolumeResult(false, -1, matcher.DisplayName,
                    $"{matcher.DisplayName} isn't in the Windows volume mixer yet (start playing something first)");
            return new VolumeResult(true, VolumeMath.ToPercent(next!.Value), matcher.DisplayName, "ok");
        }
        catch (Exception ex)
        {
            Log.Error($"Changing app volume failed (app id '{mediaAppId}')", ex);
            return new VolumeResult(false, -1, matcher.DisplayName, ex.Message);
        }
    }

    private static Task<T> RunOnMtaThread<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true, Name = "PixelTunes audio" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return tcs.Task;
    }

    // ------------------------------------------------------------------ session walking

    /// <summary>
    /// A way to change one app's volume. Normally the app's master slider (ISimpleAudioVolume).
    /// If Windows refuses that, its per-speaker-channel volumes (IChannelAudioVolume) are used
    /// instead: the app gets quieter/louder the same way, but the mixer slider may not move.
    /// </summary>
    private readonly struct VolumeHandle
    {
        private readonly IntPtr _ptr;
        public readonly bool IsChannel;
        public VolumeHandle(IntPtr ptr, bool isChannel) { _ptr = ptr; IsChannel = isChannel; }

        public int Get(out float level)
        {
            float l = 0;
            int hr = IsChannel
                ? ((delegate* unmanaged[Stdcall]<IntPtr, uint, float*, int>)Slot(_ptr, 5))(_ptr, 0, &l)   // IChannelAudioVolume::GetChannelVolume(0)
                : ((delegate* unmanaged[Stdcall]<IntPtr, float*, int>)Slot(_ptr, 4))(_ptr, &l);           // ISimpleAudioVolume::GetMasterVolume
            level = l;
            return hr;
        }

        public int Set(float level)
        {
            Guid context = Guid.Empty;
            if (!IsChannel)
                return ((delegate* unmanaged[Stdcall]<IntPtr, float, Guid*, int>)Slot(_ptr, 3))(_ptr, level, &context); // SetMasterVolume

            uint channels;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(_ptr, 3))(_ptr, &channels); // GetChannelCount
            if (hr < 0) return hr;
            if (channels == 0 || channels > 64) return unchecked((int)0x80070057); // E_INVALIDARG
            var levels = stackalloc float[(int)channels];
            for (int i = 0; i < channels; i++) levels[i] = level;
            return ((delegate* unmanaged[Stdcall]<IntPtr, uint, float*, Guid*, int>)Slot(_ptr, 6))(_ptr, channels, levels, &context); // SetAllVolumes
        }
    }

    /// <summary>Calls <paramref name="visit"/> for every app session on every active output device.</summary>
    private static void ForEachSession(Action<uint, string, string, VolumeHandle?, string?, string> visit, List<string>? failures = null)
    {
        var toRelease = new List<IntPtr>();
        try
        {
            Guid clsid = CLSID_MMDeviceEnumerator, iidEnum = IID_IMMDeviceEnumerator;
            Check(CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_ALL, ref iidEnum, out IntPtr enumerator), "opening the Windows audio device list");
            toRelease.Add(enumerator);

            IntPtr devices;
            // IMMDeviceEnumerator::EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &devices)
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, int>)Slot(enumerator, 3))(enumerator, 0, DEVICE_STATE_ACTIVE, &devices), "listing output devices");
            toRelease.Add(devices);

            uint deviceCount;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(devices, 3))(devices, &deviceCount), "counting output devices"); // GetCount

            for (uint d = 0; d < deviceCount; d++)
            {
                IntPtr device;
                if (((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(devices, 4))(devices, d, &device) < 0) continue; // Item
                toRelease.Add(device);

                Guid iidMgr = IID_IAudioSessionManager2;
                IntPtr manager;
                // IMMDevice::Activate(IID_IAudioSessionManager2, CLSCTX_ALL, NULL, &manager)
                if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, uint, IntPtr, IntPtr*, int>)Slot(device, 3))(device, &iidMgr, CLSCTX_ALL, IntPtr.Zero, &manager) < 0) continue;
                toRelease.Add(manager);

                IntPtr sessions;
                if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(manager, 5))(manager, &sessions) < 0) continue; // GetSessionEnumerator
                toRelease.Add(sessions);

                int count;
                if (((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(sessions, 3))(sessions, &count) < 0) continue; // GetCount

                for (int i = 0; i < count; i++)
                {
                    IntPtr control;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, int, IntPtr*, int>)Slot(sessions, 4))(sessions, i, &control) < 0) continue; // GetSession
                    toRelease.Add(control);

                    int qi2 = QueryInterface(control, IID_IAudioSessionControl2, out IntPtr control2);
                    if (qi2 < 0)
                    {
                        failures?.Add($"Windows refused reading a volume-mixer entry (IAudioSessionControl2, error 0x{qi2:X8})");
                        continue;
                    }
                    toRelease.Add(control2);

                    uint pid;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, uint*, int>)Slot(control2, 14))(control2, &pid) < 0 || pid == 0) continue; // GetProcessId (0 = system sounds; multi-process sessions are skipped)

                    string instanceId = "";
                    IntPtr idPtr;
                    if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(control2, 13))(control2, &idPtr) >= 0 && idPtr != IntPtr.Zero) // GetSessionInstanceIdentifier
                    {
                        instanceId = Marshal.PtrToStringUni(idPtr) ?? "";
                        Marshal.FreeCoTaskMem(idPtr);
                    }

                    string name = ProcessName(pid);
                    int state = -1;
                    ((delegate* unmanaged[Stdcall]<IntPtr, int*, int>)Slot(control, 3))(control, &state); // IAudioSessionControl::GetState

                    // Ask for each volume-related control and remember Windows' answers for Diagnostics.
                    int qiSimple = QueryInterface(control, IID_ISimpleAudioVolume, out IntPtr simple);
                    int qiSimple2 = qiSimple < 0 ? QueryInterface(control2, IID_ISimpleAudioVolume, out simple) : qiSimple;
                    int qiChannel = QueryInterface(control, IID_IChannelAudioVolume, out IntPtr channel);
                    int qiMeter = QueryInterface(control, IID_IAudioMeterInformation, out IntPtr meter);
                    if (meter != IntPtr.Zero) Marshal.Release(meter);
                    if (simple != IntPtr.Zero) toRelease.Add(simple);
                    if (channel != IntPtr.Zero) toRelease.Add(channel);
                    string probe = $"simple={Hr(qiSimple)}/{Hr(qiSimple2)} channel={Hr(qiChannel)} meter={Hr(qiMeter)} state={state}";

                    if (simple != IntPtr.Zero && qiSimple2 >= 0)
                        visit(pid, name, instanceId, new VolumeHandle(simple, isChannel: false), null, probe);
                    else if (channel != IntPtr.Zero && qiChannel >= 0)
                        visit(pid, name, instanceId, new VolumeHandle(channel, isChannel: true), null, probe);
                    else
                        visit(pid, name, instanceId, null,
                            $"Windows refused access to {name}'s volume (slider 0x{qiSimple2:X8}, channels 0x{qiChannel:X8})", probe);
                }
            }
        }
        finally
        {
            for (int i = toRelease.Count - 1; i >= 0; i--) Marshal.Release(toRelease[i]);
        }
    }

    private static string Hr(int hr) => hr >= 0 ? "ok" : $"0x{hr:X8}";

    private static IntPtr Slot(IntPtr comObject, int index) => (*(IntPtr**)comObject)[index];

    private static int QueryInterface(IntPtr obj, Guid iid, out IntPtr result) => Marshal.QueryInterface(obj, in iid, out result);

    private static string ProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName; }
        catch { return ""; }
    }

    private static void Check(int hr, string step)
    {
        if (hr < 0) throw new InvalidOperationException($"Windows refused {step} (error 0x{hr:X8})");
    }

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr instance);

    private const uint DEVICE_STATE_ACTIVE = 0x1;
    private const uint CLSCTX_ALL = 0x17;
    private static readonly Guid CLSID_MMDeviceEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static readonly Guid IID_IMMDeviceEnumerator = new("A95664D2-9614-4F35-A746-DE8DB63617E6");
    private static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
    private static readonly Guid IID_IAudioSessionControl2 = new("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D");
    private static readonly Guid IID_ISimpleAudioVolume = new("87CE5498-68D6-44E5-9215-6F7C1E3F9F5B");
    private static readonly Guid IID_IChannelAudioVolume = new("1C158861-B533-4B30-B1CF-E853E51C59B8");
    private static readonly Guid IID_IAudioMeterInformation = new("C02216F6-8C67-4B5B-9D00-D008E73E0064");
}
