using Microsoft.Extensions.Logging;

namespace MahjongApp.Services;

/// <summary>
/// 画面の ErrorBoundary で捕まえられなかったエラー（Blazor が未処理の例外として記録したもの）を受け取り、画面に知らせる。
/// 自動では送らず、利用者が「報告する」を選んだときだけ送る
/// </summary>
public class ErrorCapture
{
    private Exception? _lastNotified;

    public event Action<Exception>? Captured;

    public void Report(Exception ex)
    {
        if (ReferenceEquals(ex, _lastNotified)) return; // 同じ例外は 1 回だけ知らせる
        _lastNotified = ex;
        Captured?.Invoke(ex);
    }
}

/// <summary>
/// Blazor の未処理の例外は Critical で記録されるので、それだけを ErrorCapture に渡す
/// </summary>
public sealed class ErrorCaptureLoggerProvider : ILoggerProvider
{
    private readonly ErrorCapture _capture;

    public ErrorCaptureLoggerProvider(ErrorCapture capture) => _capture = capture;

    public ILogger CreateLogger(string categoryName) => new Logger(_capture);

    public void Dispose() { }

    private sealed class Logger : ILogger
    {
        private readonly ErrorCapture _capture;

        public Logger(ErrorCapture capture) => _capture = capture;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Critical;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Critical && exception != null) _capture.Report(exception);
        }
    }
}
