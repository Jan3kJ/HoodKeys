using Microsoft.Extensions.Logging;

namespace HoodKeys.Cli.Logging;

/// <summary>
/// The logger factory of a CLI run: every entry goes to the daily file log (Debug and up) and to the
/// console (Information and up, Debug with <c>--verbose</c>).
/// </summary>
public sealed class CliLogging : ILoggerFactory
{
    private readonly List<ILoggerProvider> _providers = [];

    private CliLogging(string logFile)
    {
        LogFile = logFile;
    }

    /// <summary>The file in use when the run started.</summary>
    public string LogFile { get; }

    public static CliLogging Create(string logDirectory, TextWriter console, bool verbose, TimeProvider time)
    {
        var logging = new CliLogging(LogPaths.FileFor(logDirectory, time.GetLocalNow()));
        var gate = new object();

        logging.AddProvider(new LineLoggerProvider(
            verbose ? LogLevel.Debug : LogLevel.Information,
            "HH:mm:ss.fff",
            time,
            line =>
            {
                lock (gate)
                {
                    console.WriteLine(line);
                }
            }));

        logging.AddProvider(new LineLoggerProvider(
            LogLevel.Debug,
            "yyyy-MM-dd HH:mm:ss.fff",
            time,
            line =>
            {
                lock (gate)
                {
                    // The day is decided per line, so a run past midnight continues in the next day's file.
                    AppendLine(LogPaths.FileFor(logDirectory, time.GetLocalNow()), line);
                }
            }));

        return logging;
    }

    public void AddProvider(ILoggerProvider provider) => _providers.Add(provider);

    public ILogger CreateLogger(string categoryName) =>
        new FanOutLogger(_providers.Select(p => p.CreateLogger(categoryName)).ToArray());

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    private static void AppendLine(string path, string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A log file that cannot be written must not stop the button handling.
        }
    }

    private sealed class FanOutLogger(ILogger[] targets) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => targets.Any(t => t.IsEnabled(logLevel));

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (var target in targets)
            {
                target.Log(logLevel, eventId, state, exception, formatter);
            }
        }
    }
}
