namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

/// <summary>
/// VoiceMeeter device driver type (matches VBVMR_DEVTYPE_*).
/// </summary>
public enum VoiceMeeterDeviceType
{
    Mme = 1,
    Wdm = 3,
    Ks = 4,
    Asio = 5,
}
