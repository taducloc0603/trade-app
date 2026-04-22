namespace TradeDesktop.App.Services;

public interface ITradeSessionFileLogger
{
    bool IsSessionActive { get; }
    string? CurrentLogFilePath { get; }

    void StartSession(DateTimeOffset startedAtLocal, string hostName);
    void Log(string message);
    void StopSession(DateTimeOffset stoppedAtLocal);
}
