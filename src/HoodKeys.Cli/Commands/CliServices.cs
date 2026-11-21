using HoodKeys.Core.Actions;
using HoodKeys.Core.Ble;

namespace HoodKeys.Cli.Commands;

/// <summary>Everything the commands touch outside the process; tests replace it with fakes.</summary>
public sealed record CliServices(IDi2Transport Transport, IKeySender Keys)
{
    /// <summary>Null means the real PowerShell runner.</summary>
    public ICommandRunner? Commands { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    public TextWriter Out { get; init; } = Console.Out;

    public TextWriter Error { get; init; } = Console.Error;

    /// <summary>Null means <see cref="Logging.LogPaths.DefaultDirectory"/>.</summary>
    public string? LogDirectory { get; init; }

    /// <summary>Wait between reconnect attempts; null means a real delay.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; init; }

    /// <summary>How long run and monitor scan to pick a device when none is named.</summary>
    public TimeSpan PickScan { get; init; } = TimeSpan.FromSeconds(5);
}
