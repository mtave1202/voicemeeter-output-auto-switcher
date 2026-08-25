namespace VoiceMeeterOutputAutoSwitcher.Application.Abstractions;

public sealed record VoiceMeeterBusStatus(
    bool IsConnected,
    string? A1DeviceName,
    string? A2DeviceName,
    string? A3DeviceName);
