using System.Globalization;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Cli.Logging;

/// <summary>
/// Formats each log entry as one line and hands it to a sink. The console and the file log are both this
/// provider with a different sink, minimum level and time format.
/// </summary>
public sealed class LineLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimum;
    private readonly string _timeFormat;
    private readonly TimeProvider _time;
    private readonly Action<string> _sink;

    public LineLoggerProvider(LogLevel minimum, string timeFormat, TimeProvider time, Action<string> sink)
    {
        _minimum = minimum;
        _timeFormat = timeFormat;
        _time = time;
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName) => new LineLogger(this, ShortName(categoryName));

    public void Dispose()
    {
    }

    private static string ShortName(string category) => category[(category.LastIndexOf('.') + 1)..];

    private static string Tag(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };

    private sealed class LineLogger(LineLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= owner._minimum;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var stamp = owner._time.GetLocalNow().ToString(owner._timeFormat, CultureInfo.InvariantCulture);
            var line = $"{stamp}  {Tag(logLevel)}  {category}: {formatter(state, exception)}";
            owner._sink(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
        }
    }
}
