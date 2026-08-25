using System.Runtime.InteropServices;

namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

internal static class VoiceMeeterNative
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int Login();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int Logout();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetVoicemeeterType(out int type);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetVoicemeeterVersion(out int version);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int IsParametersDirty();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int GetParameterStringA(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        IntPtr stringBuffer);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetParameterStringA(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        [MarshalAs(UnmanagedType.LPStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int SetParameterFloat(
        [MarshalAs(UnmanagedType.LPStr)] string paramName,
        float value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int OutputGetDeviceNumber();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int OutputGetDeviceDescA(
        int index,
        out int type,
        IntPtr deviceName,
        IntPtr hardwareId);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
}
