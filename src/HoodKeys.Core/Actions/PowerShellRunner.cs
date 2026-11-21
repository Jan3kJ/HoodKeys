using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace HoodKeys.Core.Actions;

/// <summary>Runs commands with <c>powershell.exe -NoProfile -Command</c> and logs each exit code when the process ends.</summary>
public sealed class PowerShellRunner(ILogger<PowerShellRunner> logger) : ICommandRunner
{
    public Task<int> Start(string command)
    {
        var completion = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        Process? process = null;
        try
        {
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add(command);

            process = new Process { StartInfo = start, EnableRaisingEvents = true };
            var started = process;
            process.Exited += (_, _) =>
            {
                var exitCode = started.ExitCode;
                logger.LogInformation("Command exited with code {ExitCode}: {Command}", exitCode, command);
                started.Dispose();
                completion.TrySetResult(exitCode);
            };
            process.Start();
            logger.LogInformation("Command started (pid {Pid}): {Command}", process.Id, command);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Command could not be started: {Command}", command);
            process?.Dispose();
            completion.TrySetResult(-1);
        }

        return completion.Task;
    }
}
