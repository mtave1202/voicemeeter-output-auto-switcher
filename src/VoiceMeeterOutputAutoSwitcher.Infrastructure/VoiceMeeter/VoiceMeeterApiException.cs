namespace VoiceMeeterOutputAutoSwitcher.Infrastructure.VoiceMeeter;

public sealed class VoiceMeeterApiException : Exception
{
    public int ErrorCode { get; }

    public VoiceMeeterApiException(string message, int errorCode)
        : base($"{message} (code={errorCode})")
    {
        ErrorCode = errorCode;
    }
}
