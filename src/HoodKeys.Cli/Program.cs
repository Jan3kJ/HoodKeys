using System.CommandLine;
using HoodKeys.Cli.Commands;
using HoodKeys.Cli.Spike;
using HoodKeys.Core;
using HoodKeys.Core.Actions;
using HoodKeys.Core.Ble;

// System.CommandLine provides --version (the assembly's informational version).
var transport = new WinRtDi2Transport();
var root = new RootCommand($"{ProductInfo.Name}: turns Di2 button presses into keystrokes or commands");
root.Options.Add(CliCommands.Verbose);
foreach (var command in CliCommands.Create(new CliServices(transport, new SendInputKeySender())))
{
    root.Subcommands.Add(command);
}

foreach (var command in SpikeCommands.Create(transport))
{
    root.Subcommands.Add(command);
}

return await root.Parse(args).InvokeAsync();
