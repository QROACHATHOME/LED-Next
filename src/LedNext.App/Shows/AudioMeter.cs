using System.Runtime.InteropServices;
using LedNext.Core;
using LedNext.Core.Shows;

namespace LedNext.App.Shows;

/// <summary>
/// The sound level of Windows' default speakers, from the endpoint's peak meter (<c>IAudioMeterInformation</c>, the
/// meter the Sound control panel shows). It records nothing and changes nothing: it only reads the level, ~30 times a
/// second on its own thread, while at least one user has called <see cref="Acquire"/>. The default device is looked
/// up again every few seconds, so switching speakers works. Never throws: without sound devices the level stays 0.
/// </summary>
public sealed class AudioMeter : IAudioLevel, IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(33), Refresh = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private readonly HashSet<string> _users = new();
    private Thread? _thread;
    private volatile bool _run;
    private float _left, _right;
    private bool _loggedFailure;

    public (float Left, float Right) Peak => (Volatile.Read(ref _left), Volatile.Read(ref _right));
    public bool Running => _run;

    public void Acquire(string user)
    {
        lock (_gate)
        {
            _users.Add(user);
            if (_run) return;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "LED-Next sound level" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
            AppLog.Info($"Sound level: listening to the default speakers ({user}).");
        }
    }

    public void Release(string user)
    {
        Thread? stop = null;
        lock (_gate)
        {
            if (!_users.Remove(user) || _users.Count > 0 || !_run) return;
            _run = false;
            stop = _thread;
            _thread = null;
        }
        stop?.Join(TimeSpan.FromSeconds(1));
        Volatile.Write(ref _left, 0);
        Volatile.Write(ref _right, 0);
        AppLog.Info("Sound level: stopped.");
    }

    private void Loop()
    {
        IAudioMeterInformation? meter = null;
        var lookedUp = DateTime.MinValue;
        var peaks = new float[8];
        while (_run)
        {
            try
            {
                if (meter is null || DateTime.UtcNow - lookedUp > Refresh)
                {
                    Release(ref meter);
                    lookedUp = DateTime.UtcNow;
                    meter = OpenDefault();
                }
                if (meter is not null)
                {
                    float l = 0, r = 0;
                    if (meter.GetMeteringChannelCount(out int n) == 0 && n >= 2 && n <= peaks.Length &&
                        meter.GetChannelsPeakValues(n, peaks) == 0)
                    {
                        l = peaks[0];
                        r = peaks[1];
                        for (int i = 2; i < n; i++) { l = Math.Max(l, peaks[i] * 0.5f); r = Math.Max(r, peaks[i] * 0.5f); } // centre, rears
                    }
                    else if (meter.GetPeakValue(out float p) == 0) l = r = p;
                    Volatile.Write(ref _left, l);
                    Volatile.Write(ref _right, r);
                }
            }
            catch (Exception ex)
            {
                Release(ref meter);
                if (!_loggedFailure) { _loggedFailure = true; AppLog.Warn($"Sound level: could not read the speakers' meter ({ex.Message}); trying again."); }
            }
            Thread.Sleep(Poll);
        }
        Release(ref meter);
    }

    private IAudioMeterInformation? OpenDefault()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            if (enumerator.GetDefaultAudioEndpoint(0 /* eRender */, 1 /* eMultimedia */, out device) != 0 || device is null) return null;
            var iid = typeof(IAudioMeterInformation).GUID;
            if (device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var obj) != 0) return null;
            return obj as IAudioMeterInformation;
        }
        catch (Exception ex)
        {
            if (!_loggedFailure) { _loggedFailure = true; AppLog.Warn($"Sound level: no default speakers ({ex.Message})."); }
            return null;
        }
        finally
        {
            if (device is not null) Marshal.ReleaseComObject(device);
            if (enumerator is not null) Marshal.ReleaseComObject(enumerator);
        }
    }

    private static void Release(ref IAudioMeterInformation? meter)
    {
        if (meter is null) return;
        try { Marshal.ReleaseComObject(meter); } catch { /* gone already */ }
        meter = null;
    }

    public void Dispose()
    {
        lock (_gate) _users.Clear();
        _run = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] values);
        [PreserveSig] int QueryHardwareSupport(out int mask);
    }
}
