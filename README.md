# HoodKeys

HoodKeys turns the button presses of a Shimano Di2 bike into keystrokes or PowerShell commands on your Windows PC. You assign the extra buttons (for example the satellite buttons on the hoods) to D-Fly channels in Shimano's E-TUBE Project, and HoodKeys listens over Bluetooth LE and sends a key such as `I` or `K` to the foreground window, so you can shift or steer in apps like MyWhoosh from the bike.

HoodKeys is an unofficial project. It is not affiliated with or endorsed by Shimano. Shimano and Di2 are trademarks of Shimano Inc.

## Requirements

- Windows 10 or 11 with Bluetooth LE.
- A Shimano Di2 bike with a unit that offers D-Fly (for example the RD-RX827 rear derailleur).
- Nothing else: the release `hoodkeys.exe` includes .NET, so you need no .NET install.

## Download

Get `hoodkeys-<version>-win-x64.zip` from the [GitHub Releases](https://github.com/Jan3kJ/HoodKeys/releases) page. It contains:

- `hoodkeys.exe`
- `mywhoosh.json`, an example config
- `LICENSE`

Unzip it anywhere. The exe is not code-signed, so Windows SmartScreen may show "Windows protected your PC" on first start: click **More info**, then **Run anyway**.

## Set up the bike

In E-TUBE Project, assign each button you want to use to one of the D-Fly channels 1 to 4. Buttons you do not assign to a D-Fly channel keep shifting as before.

## Commands

Run these in a terminal (`hoodkeys --help` lists them; `hoodkeys --version` shows the version).

### `hoodkeys scan`

Lists the Di2 units in range (name, address, signal strength), then other Bluetooth devices.

- `--seconds <n>`: how long to scan (default 10).
- `--verbose`: also show Debug entries on the console.

Exit code 0 if a Di2 unit was found, 1 if not.

### `hoodkeys monitor`

Prints every button event live until Ctrl+C. Use it to check which channel a button is on. Events are `ShortPress`, `LongPress`, `LongHold`, `DoubleTap` and `Release`.

- `--device <address|name>`: a Di2 address (`AA:BB:CC:DD:EE:FF`) or a name prefix such as `RDRX827`. Default: the device in your config, else the first Di2 found.
- `--verbose`: also show Debug entries (raw reports) on the console.

### `hoodkeys run`

Turns button events into the keys and commands from your config until Ctrl+C.

- `--config <path>`: config file (default `%APPDATA%\HoodKeys\config.json`).
- `--device <address|name>`: as for `monitor`.
- `--verbose`: also show Debug entries on the console.

If the config has errors, they are printed as `file: field.path: message` and the exit code is 1.

## Config

The config is a JSON file at `%APPDATA%\HoodKeys\config.json` (or the path given with `--config`). Comments and trailing commas are allowed. Start from `mywhoosh.json`:

```json
{
  "channels": {
    "1": { "short": { "key": "I" }, "long": "repeat", "repeatIntervalMs": 150 },
    "2": { "short": { "key": "K" }, "long": "repeat", "repeatIntervalMs": 150 }
  }
}
```

- `device` (optional): `{ "name": "RD-RX827" }` and/or `{ "address": "AA:BB:CC:DD:EE:FF" }` picks one Di2 unit.
- `channels`: `"1"` to `"4"`, the D-Fly channels you assigned in E-TUBE.
- An action has exactly one of `key` or `command`. `key` is a key or combo such as `I`, `F5`, `Space`, `Ctrl+Shift+X` (modifiers Ctrl, Shift, Alt, Win). `command` is a PowerShell command, run as `powershell.exe -NoProfile -Command <command>` without a window.
- `short`: the action for a short press.
- `long`: either an action of its own, or `"repeat"`, which repeats the short action while the button is held (needs a `short`). `repeatIntervalMs` (default 150) is only valid with `"repeat"`.
- Unknown fields are errors, so typos do not pass silently.

A quick double tap runs the short action twice. A channel or press type without config does nothing.

## Log file

`%LOCALAPPDATA%\HoodKeys\logs\hoodkeys-YYYY-MM-DD.log`, one file per day with Debug detail, including every key or command sent and its latency. `monitor` and `run` print the path when they start.

## Troubleshooting

- **`scan` does not find the bike:** the Di2 unit sleeps when idle. Wake it with a button press or by moving the bike, then scan again.
- **Keys do not arrive in the app:** keys go to the foreground window. If the target app runs as administrator, run `hoodkeys` as administrator too.
- **The link drops:** `run` and `monitor` reconnect on their own, retrying after 1 s and backing off to 5 s, and any key repeat in progress stops. Wake the bike if it does not come back.
- **Something else:** run with `--verbose` and look at the log file.

## Building from source

With the .NET 10 SDK: `dotnet test && dotnet format --verify-no-changes`. See [docs/architecture.md](docs/architecture.md) for how it is built.

## License

[MIT](LICENSE)
