namespace HoodKeys.Core.Actions;

/// <summary>Starts a PowerShell command without waiting for it.</summary>
public interface ICommandRunner
{
    /// <summary>
    /// Starts the command and returns at once. The task completes with the exit code when the command ends
    /// (-1 if it could not be started); callers need not await it.
    /// </summary>
    Task<int> Start(string command);
}
