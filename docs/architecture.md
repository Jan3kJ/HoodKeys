# Architecture

HoodKeys turns Shimano Di2 D-Fly button presses into keystrokes or PowerShell commands.

See the [README](../README.md) for installation and use.

## Layout

- `src/HoodKeys.Core`: library with all behavior (Bluetooth behind an interface, decoder, config, actions). It contains no console code; an architecture test fails if it references `System.Console`.
- `src/HoodKeys.Cli`: thin command line, assembly `hoodkeys`. Commands: `scan`, `monitor`, `run` (see Commands), `--version`, and the hidden diagnostic commands (see Bluetooth).
- `tests/HoodKeys.Core.Tests`: xUnit tests of Core, including the architecture test.

All projects target `net10.0-windows10.0.19041.0`; shared settings (nullable, implicit usings, warnings as errors) live in `Directory.Build.props`.

## D-Fly decoder

`HoodKeys.Core.Dfly.DflyDecoder` is a pure, stateful decoder. `Decode(byte[])` takes one D-Fly report (byte 0 a report counter that is ignored, bytes 1 to 4 one byte per channel: `0xF0` until the channel's first press, then high nibble = event and low nibble = per-channel event counter) and returns `Di2Event(Channel 1-4, Kind)` for every channel whose byte changed since the previous report: high nibble `1` is `ShortPress` (no release follows), `2` `LongPress`, `3` `LongHold` (repeats while held), `4` `DoubleTap` (a quick double tap in one report), `0` `Release` (after a long press). Other high nibbles are ignored. Every channel starts at `0xF0`, as on a new link, so the first report is decoded like any other (a channel going from `0xF0` to a known nibble is one event); `Reset()` returns all channels to `0xF0` for a new link. Identical reports give no events, short reports decode the channels present, extra bytes are ignored, and nothing throws. `ActionDispatcher` runs the short action twice for a `DoubleTap` (nothing if the channel has no short action) and ignores `LongHold` (it runs its own repeat timer). Tests replay the byte fixtures in `tests/HoodKeys.Core.Tests/Fixtures` (one report per line: hex bytes, `=>`, expected events); `b1-log.txt` and `c2-monitor.txt` are recorded sessions, replayed against full expected event lists.

## Config

`HoodKeys.Core.Config.ConfigLoader` reads the JSON config (comments and trailing commas allowed) from `--config` or `ConfigPaths.DefaultPath()` (`%APPDATA%\HoodKeys\config.json`). `Load(path)` and `Parse(json, file)` return a `ConfigLoadResult`: a `HoodKeysConfig` or a list of `ConfigError(File, Path, Message)`, printed as `file: field.path: message`. All errors are collected, not just the first.

```json
{
  "device": { "name": "...", "address": "..." },
  "channels": {
    "1": { "short": { "key": "I" }, "long": "repeat", "repeatIntervalMs": 150 },
    "3": { "short": { "key": "Ctrl+Shift+X" }, "long": { "command": "<powershell>" } }
  }
}
```

- `device` is optional (name or address); channels are `"1"` to `"4"`.
- An action has exactly one of `key` or `command`; a command must not be empty.
- `long` is an action or the string `"repeat"`, which repeats the channel's short action and needs one. `repeatIntervalMs` (default 150, whole number above 0) is only valid with `"repeat"`.
- Unknown fields are errors, so typos do not pass silently.

`HoodKeys.Core.Keys.KeyNames` is the key-name table (case-insensitive: letters, digits, F1 to F24, numpad, arrows, editing and punctuation keys, media keys) with Windows virtual-key codes. `TryParse("Ctrl+Shift+X")` gives a `KeyCombo` of modifier codes (Ctrl, Shift, Alt, Win; each at most once) plus one main key; config validation and the key action share it. `samples/mywhoosh.json` (channel 1 `I`, channel 2 `K`, both repeating) is copied into the test output and loaded by a test.

## Actions

`HoodKeys.Core.Actions.ActionDispatcher.Handle(Di2Event)` maps an event to the channel's configured action:

- `ShortPress` runs the short action; `LongPress` runs the long action once, or in repeat mode starts a timer that runs the short action every `repeatIntervalMs` (first repeat one interval after the long press); `Release` stops that channel's repeat; `StopAllRepeats()` stops every repeat and logs `Repeat stopped on channel N (link lost)`. `Di2Session.LinkLost` is raised once when a subscribed link drops or the run is cancelled while a link is up, and `hoodkeys run` wires it to `StopAllRepeats()`, because a dropped link never delivers a release. A channel or press type without config does nothing.
- Only keys and commands written in the config are ever sent or run; events carry no payload that reaches a command.
- Time comes from an injectable `TimeProvider` (timers via `CreateTimer`), so tests advance a fake clock instead of waiting.
- A failing action is logged and does not stop later events.
- Logging goes through `Microsoft.Extensions.Logging`: `Event received <event> at <utc>` and one line per sent key or command, `Action sent for <event>: <action> received_at=<utc> sent_at=<utc> latency_ms=<n.nn>` (grep `latency_ms=` for the latency figures).

`IKeySender` is implemented by `SendInputKeySender` (Win32 `SendInput`, one call per combo: modifiers down in written order, key down, key up, modifiers up in reverse; navigation keys flagged extended). `KeyInputPlan` builds that sequence and is unit-tested. `ICommandRunner.Start` is implemented by `PowerShellRunner`: `powershell.exe -NoProfile -Command <command>` with no window, returns at once with a task for the exit code, which is logged when the process ends.

## Bluetooth

`HoodKeys.Core.Ble.IDi2Transport` is the Bluetooth seam: `ScanAsync(duration)` returns `AdvertisedDevice(Address, Name, Rssi, ServiceUuids)` merged over all advertisements, and `ConnectAsync(address)` returns an `IDi2Link` with `DiscoverAsync` (all services and characteristics, uncached), `ReadDflyAsync` (null if the characteristic is missing or not readable), `SubscribeDflyAsync` (indications, falling back to notifications) and the events `ReportReceived(RawReport)` and `ConnectionChanged`. `WinRtDi2Transport` implements it over the WinRT API (`Windows.Devices.Bluetooth`), active scanning, no pairing. `DflyGatt` holds the D-Fly service and characteristic UUIDs; `BleAddress` formats and parses `AA:BB:CC:DD:EE:FF`; `Di2Names` is the name heuristic that highlights likely Shimano units in a scan. `Di2Session(transport, target, logger, delay?)` keeps one unit connected: `target` is an address or a device-name prefix (such as `RDRX827`, found by scan on each attempt, strongest signal wins). `RunAsync(ct)` connects, subscribes, feeds every `RawReport` through a `DflyDecoder` and raises `EventReceived(Di2Notification(Di2Event, report timestamp))`. When connecting fails or the link drops it retries after 1 s, doubling to a 5 s cap, until cancelled (cancellation returns normally); the backoff starts over once a link has subscribed. Every new link calls `DflyDecoder.Reset()`, so the first report after a reconnect is decoded like any other. The delay is an injected `Func<TimeSpan, CancellationToken, Task>`, so tests run the backoff without waiting. Logging: Information for connecting, connected, subscribed (indicate or notify), disconnected, each retry with its delay, and the first raw report in hex after every connect; Debug for every raw report.

Hidden CLI commands (not in `--help`), in `src/HoodKeys.Cli/Spike`:

- `hoodkeys spike-scan`: 10 s advertisement watch; lists address, RSSI, name and service UUIDs, `*` for the D-Fly service, `?` for a Shimano-looking name.
- `hoodkeys spike-monitor <address>`: connects, lists services and characteristics, reads and subscribes to D-Fly, prints every payload as hex (the fixture format) with the decoder's events, and appends every line with a timestamp to `spike-monitor.log` next to the exe. Ctrl+C stops it.



## Commands

`src/HoodKeys.Cli/Commands/CliCommands.cs` wires the three commands (`Program.cs` only builds the root); everything they touch outside the process (transport, key sender, command runner, clock, output, log directory) comes from `CliServices`, so tests run them with fakes.

- `hoodkeys scan [--seconds 10]`: scans, lists Di2 units first (name, address, RSSI), then other devices. Exit 0 when a Di2 was found, 1 when none.
- `hoodkeys monitor [--device ADDRESS|NAME]`: connects through `Di2Session` and prints `HH:mm:ss.fff  channel N  ShortPress|LongPress|LongHold|DoubleTap|Release  <hex of the report>` per event until Ctrl+C.
- `hoodkeys run [--config PATH] [--device ADDRESS|NAME]`: loads the config (`--config`, else `ConfigPaths.DefaultPath()`; errors go to stderr and the exit code is 1), then feeds session events to `ActionDispatcher` with `SendInputKeySender` and `PowerShellRunner` until Ctrl+C.
- Device choice for `monitor` and `run` (`DeviceSelector`): `--device`, else the config's device (address before name), else the strongest Di2 a 5 s scan finds; exit 1 when there is none.
- `--verbose` (any position) adds Debug entries to the console. `monitor` and `run` print the log file path first.

Every command logs through `CliLogging` (an `ILoggerFactory` over `Microsoft.Extensions.Logging.Abstractions`, no extra package): the console gets Information and up, the file gets Debug and up. The file is `%LOCALAPPDATA%\HoodKeys\logs\hoodkeys-YYYY-MM-DD.log` (local date, chosen per line; one line per entry, `yyyy-MM-dd HH:mm:ss.fff  LVL  Category: message`). A log file that cannot be written is skipped silently.

## Test build

CI (`.github/workflows/ci.yml`) publishes the single-file `hoodkeys.exe` on every push to `main` and uploads it with `samples/mywhoosh.json` as the workflow artifact `hoodkeys-test-build` (kept 14 days); no release is made.

## Check

`dotnet test && dotnet format --verify-no-changes`, run in `product/`.
