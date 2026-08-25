using System.Runtime.InteropServices;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

/// <summary>
/// Thin wrapper around VoiceMeeter Remote API.
/// Intentionally exposes A2/A3 control only; A1 is never written.
/// </summary>
public sealed class VoiceMeeterRemoteClient : IDisposable
{
    public const int BusA1 = 0;
    public const int BusA2 = 1;
    public const int BusA3 = 2;

    private const int StringBufferChars = 512;

    private IntPtr _module;
    private VoiceMeeterNative.Login? _login;
    private VoiceMeeterNative.Logout? _logout;
    private VoiceMeeterNative.GetVoicemeeterType? _getType;
    private VoiceMeeterNative.GetVoicemeeterVersion? _getVersion;
    private VoiceMeeterNative.IsParametersDirty? _isDirty;
    private VoiceMeeterNative.GetParameterStringA? _getString;
    private VoiceMeeterNative.SetParameterStringA? _setString;
    private VoiceMeeterNative.SetParameterFloat? _setFloat;
    private VoiceMeeterNative.OutputGetDeviceNumber? _outputDeviceNumber;
    private VoiceMeeterNative.OutputGetDeviceDescA? _outputDeviceDesc;
    private bool _loggedIn;
    private bool _disposed;

    public bool IsLoggedIn => _loggedIn;

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_loggedIn)
        {
            return;
        }

        LoadNativeLibrary();

        var loginResult = _login!();
        if (loginResult < 0)
        {
            throw new VoiceMeeterApiException("VBVMR_Login failed.", loginResult);
        }

        _loggedIn = true;

        // Initialize / refresh parameter cache as recommended by the API docs.
        _ = _isDirty!();
    }

    public bool IsServerAvailable()
    {
        EnsureLoggedIn();
        return _isDirty!() >= 0;
    }

    public VoiceMeeterProductType GetProductType()
    {
        EnsureLoggedIn();
        EnsureServerAvailable();

        var code = _getType!(out var type);
        if (code < 0)
        {
            throw new VoiceMeeterApiException("VBVMR_GetVoicemeeterType failed.", code);
        }

        return type switch
        {
            1 => VoiceMeeterProductType.Standard,
            2 => VoiceMeeterProductType.Banana,
            3 => VoiceMeeterProductType.Potato,
            _ => VoiceMeeterProductType.Unknown,
        };
    }

    public Version GetVersion()
    {
        EnsureLoggedIn();
        EnsureServerAvailable();

        var code = _getVersion!(out var packed);
        if (code < 0)
        {
            throw new VoiceMeeterApiException("VBVMR_GetVoicemeeterVersion failed.", code);
        }

        var v1 = (packed >> 24) & 0xFF;
        var v2 = (packed >> 16) & 0xFF;
        var v3 = (packed >> 8) & 0xFF;
        var v4 = packed & 0xFF;
        return new Version(v1, v2, v3, v4);
    }

    public string GetBusDeviceName(int busIndex)
    {
        EnsureLoggedIn();
        EnsureServerAvailable();
        EnsurePhysicalBusIndex(busIndex);

        return GetParameterString($"Bus[{busIndex}].Device.name");
    }

    public void SetA2WdmDevice(string? deviceName) => SetBusWdmDevice(BusA2, deviceName);

    public void SetA3WdmDevice(string? deviceName) => SetBusWdmDevice(BusA3, deviceName);

    public void ClearA2Device() => SetA2WdmDevice(string.Empty);

    public void ClearA3Device() => SetA3WdmDevice(string.Empty);

    public void RestartAudioEngine()
    {
        EnsureLoggedIn();
        EnsureServerAvailable();

        var code = _setFloat!("Command.Restart", 1f);
        if (code < 0)
        {
            throw new VoiceMeeterApiException("Failed to set Command.Restart.", code);
        }
    }

    public IReadOnlyList<VoiceMeeterOutputDevice> GetOutputDevices()
    {
        EnsureLoggedIn();

        var count = _outputDeviceNumber!();
        if (count < 0)
        {
            throw new VoiceMeeterApiException("VBVMR_Output_GetDeviceNumber failed.", count);
        }

        var devices = new List<VoiceMeeterOutputDevice>(count);
        var nameBuffer = Marshal.AllocHGlobal(StringBufferChars);
        var hardwareBuffer = Marshal.AllocHGlobal(StringBufferChars);
        try
        {
            for (var i = 0; i < count; i++)
            {
                ZeroBuffer(nameBuffer, StringBufferChars);
                ZeroBuffer(hardwareBuffer, StringBufferChars);

                var code = _outputDeviceDesc!(i, out var type, nameBuffer, hardwareBuffer);
                if (code < 0)
                {
                    throw new VoiceMeeterApiException(
                        $"VBVMR_Output_GetDeviceDescA failed for index {i}.",
                        code);
                }

                devices.Add(new VoiceMeeterOutputDevice(
                    i,
                    (VoiceMeeterDeviceType)type,
                    Marshal.PtrToStringAnsi(nameBuffer) ?? string.Empty,
                    Marshal.PtrToStringAnsi(hardwareBuffer) ?? string.Empty));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(nameBuffer);
            Marshal.FreeHGlobal(hardwareBuffer);
        }

        return devices;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_loggedIn && _logout is not null)
        {
            try
            {
                _ = _logout();
            }
            catch
            {
                // Best-effort logout on dispose.
            }

            _loggedIn = false;
        }

        if (_module != IntPtr.Zero)
        {
            _ = VoiceMeeterNative.FreeLibrary(_module);
            _module = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void SetBusWdmDevice(int busIndex, string? deviceName)
    {
        EnsureLoggedIn();
        EnsureServerAvailable();

        if (busIndex is not (BusA2 or BusA3))
        {
            throw new InvalidOperationException(
                "Only A2/A3 may be changed by this client. A1 is intentionally read-only.");
        }

        var value = deviceName ?? string.Empty;
        var code = _setString!($"Bus[{busIndex}].Device.wdm", value);
        if (code < 0)
        {
            throw new VoiceMeeterApiException(
                $"Failed to set Bus[{busIndex}].Device.wdm.",
                code);
        }
    }

    private string GetParameterString(string paramName)
    {
        var buffer = Marshal.AllocHGlobal(StringBufferChars);
        try
        {
            ZeroBuffer(buffer, StringBufferChars);
            var code = _getString!(paramName, buffer);
            if (code < 0)
            {
                throw new VoiceMeeterApiException(
                    $"VBVMR_GetParameterStringA failed for '{paramName}'.",
                    code);
            }

            return Marshal.PtrToStringAnsi(buffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void LoadNativeLibrary()
    {
        if (_module != IntPtr.Zero)
        {
            return;
        }

        var dllPath = VoiceMeeterInstallLocator.FindRemoteDllPath();
        _module = VoiceMeeterNative.LoadLibrary(dllPath);
        if (_module == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"LoadLibrary failed for '{dllPath}'. Win32Error={Marshal.GetLastWin32Error()}");
        }

        _login = GetDelegate<VoiceMeeterNative.Login>("VBVMR_Login");
        _logout = GetDelegate<VoiceMeeterNative.Logout>("VBVMR_Logout");
        _getType = GetDelegate<VoiceMeeterNative.GetVoicemeeterType>("VBVMR_GetVoicemeeterType");
        _getVersion = GetDelegate<VoiceMeeterNative.GetVoicemeeterVersion>("VBVMR_GetVoicemeeterVersion");
        _isDirty = GetDelegate<VoiceMeeterNative.IsParametersDirty>("VBVMR_IsParametersDirty");
        _getString = GetDelegate<VoiceMeeterNative.GetParameterStringA>("VBVMR_GetParameterStringA");
        _setString = GetDelegate<VoiceMeeterNative.SetParameterStringA>("VBVMR_SetParameterStringA");
        _setFloat = GetDelegate<VoiceMeeterNative.SetParameterFloat>("VBVMR_SetParameterFloat");
        _outputDeviceNumber = GetDelegate<VoiceMeeterNative.OutputGetDeviceNumber>("VBVMR_Output_GetDeviceNumber");
        _outputDeviceDesc = GetDelegate<VoiceMeeterNative.OutputGetDeviceDescA>("VBVMR_Output_GetDeviceDescA");
    }

    private T GetDelegate<T>(string exportName)
        where T : Delegate
    {
        var proc = VoiceMeeterNative.GetProcAddress(_module, exportName);
        if (proc == IntPtr.Zero)
        {
            throw new EntryPointNotFoundException(
                $"Export '{exportName}' was not found in VoicemeeterRemote DLL.");
        }

        return Marshal.GetDelegateForFunctionPointer<T>(proc);
    }

    private void EnsureLoggedIn()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_loggedIn)
        {
            throw new InvalidOperationException("Call Connect() before using VoiceMeeterRemoteClient.");
        }
    }

    private void EnsureServerAvailable()
    {
        if (!IsServerAvailable())
        {
            throw new VoiceMeeterApiException(
                "VoiceMeeter is not running (remote server unavailable).",
                -2);
        }
    }

    private static void EnsurePhysicalBusIndex(int busIndex)
    {
        if (busIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(busIndex));
        }
    }

    private static void ZeroBuffer(IntPtr buffer, int size)
    {
        for (var i = 0; i < size; i++)
        {
            Marshal.WriteByte(buffer, i, 0);
        }
    }
}
