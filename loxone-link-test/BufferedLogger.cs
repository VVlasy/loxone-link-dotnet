using Microsoft.Extensions.Logging;

namespace loxonelinktest;

public class BufferedLogger : ILogger
{
    public enum LogLevel
    {
        Debug = 0,
        Information = 1,
        Warning = 2,
        Error = 3
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; init; }
        public LogLevel Level { get; init; }
        public string Message { get; init; } = string.Empty;
        public Exception? Exception { get; init; }
        public override string ToString()
        {
            var ts = Timestamp.ToString("HH:mm:ss.fff");
            var lvl = Level switch
            {
                LogLevel.Debug => "DEBUG",
                LogLevel.Information => "INFO ",
                LogLevel.Warning => "WARN ",
                LogLevel.Error => "ERROR",
                _ => "INFO "
            };
            var msg = $"[{lvl}] {ts} {Message}";
            if (Exception != null)
            {
                msg += $": {Exception.Message}";
            }
            return msg;
        }
    }

    private readonly object _lock = new();
    private readonly List<LogEntry> _buffer = new();
    private readonly int _capacity;

    // When true, logs are written to console live
    public bool LiveOutputEnabled { get; set; } = false;

    // Current display filter
    public LogLevel MinimumLevel { get; private set; } = LogLevel.Information;

    public BufferedLogger(int capacity = 2000)
    {
        _capacity = Math.Max(100, capacity);
    }

    // Custom logging methods for backward compatibility
    public void LogDebug(string message) => Add(LogLevel.Debug, message, null);
    public void LogInformation(string message) => Add(LogLevel.Information, message, null);
    public void LogWarning(string message) => Add(LogLevel.Warning, message, null);
    public void LogError(Exception exception, string message) => Add(LogLevel.Error, message, exception);

    // Microsoft.Extensions.Logging.ILogger implementation
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
    {
        var customLogLevel = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Debug or Microsoft.Extensions.Logging.LogLevel.Trace => LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => LogLevel.Information,
            Microsoft.Extensions.Logging.LogLevel.Warning => LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error or Microsoft.Extensions.Logging.LogLevel.Critical => LogLevel.Error,
            _ => LogLevel.Information
        };
        return customLogLevel >= MinimumLevel;
    }

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        var customLogLevel = logLevel switch
        {
            Microsoft.Extensions.Logging.LogLevel.Debug or Microsoft.Extensions.Logging.LogLevel.Trace => LogLevel.Debug,
            Microsoft.Extensions.Logging.LogLevel.Information => LogLevel.Information,
            Microsoft.Extensions.Logging.LogLevel.Warning => LogLevel.Warning,
            Microsoft.Extensions.Logging.LogLevel.Error or Microsoft.Extensions.Logging.LogLevel.Critical => LogLevel.Error,
            _ => LogLevel.Information
        };
        Add(customLogLevel, message, exception);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _buffer.Clear();
        }
    }

    public LogLevel CycleMinimumLevel()
    {
        MinimumLevel = MinimumLevel switch
        {
            LogLevel.Debug => LogLevel.Information,
            LogLevel.Information => LogLevel.Warning,
            LogLevel.Warning => LogLevel.Error,
            _ => LogLevel.Debug
        };
        return MinimumLevel;
    }

    public IReadOnlyList<LogEntry> Snapshot(int lastN = 500)
    {
        lock (_lock)
        {
            var filtered = _buffer.Where(e => e.Level >= MinimumLevel).ToList();
            if (filtered.Count <= lastN) return filtered.ToArray();
            return filtered.Skip(Math.Max(0, filtered.Count - lastN)).ToArray();
        }
    }

    private void Add(LogLevel level, string message, Exception? ex)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Exception = ex
        };

        lock (_lock)
        {
            _buffer.Add(entry);
            if (_buffer.Count > _capacity)
            {
                var remove = _buffer.Count - _capacity;
                _buffer.RemoveRange(0, remove);
            }
        }

        if (LiveOutputEnabled && level >= MinimumLevel)
        {
            Console.WriteLine(entry.ToString());
        }
    }
}
