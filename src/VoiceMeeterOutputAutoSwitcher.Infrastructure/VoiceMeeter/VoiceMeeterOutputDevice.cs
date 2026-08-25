namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

public sealed record VoiceMeeterOutputDevice(
    int Index,
    VoiceMeeterDeviceType DeviceType,
    string Name,
    string HardwareId);
