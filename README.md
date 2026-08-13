# SundayReady

A boot-time preflight checklist for the A/V stations at Trinity Baptist Church.

Every Sunday an operator sits down and has to get a set of things right before the service
starts. SundayReady turns that tribal knowledge into a checklist that runs at logon — some
items the operator ticks, some launch software, and some the app verifies for itself by
checking whether a process is running or an API answers.

**One binary runs on every station.** What differs between stations is JSON, not code.
Adding a station is a config file, never a fork.

## Setting up a booth PC

1. Download `SundayReady-win-x64.zip` from the [latest release][releases] and unzip it
   somewhere **writable** — `%LOCALAPPDATA%\Programs\SundayReady` is a good choice.
   Do not use `C:\Program Files`: the app updates itself in place and cannot write there.
2. Edit `station.json` for that station (see below), or delete it to fall back to hostname
   auto-detect.
3. Put the station's checklist files in `checklists\`.
4. Create a Task Scheduler task: **At log on**, delay **30 seconds**, restart on failure.
   `shell:startup` races with the services the verifiers check for.

Nothing needs the .NET runtime installed — releases are self-contained.

## Where things live

| What | Where |
|---|---|
| Checklists, `station.json` | Next to the exe. Edit freely; no rebuild needed. |
| Today's checked state | `%LOCALAPPDATA%\SundayReady\state.json` |
| Completion logs | `%LOCALAPPDATA%\SundayReady\logs\<date>_<station>.log` |
| Staged updates | `%LOCALAPPDATA%\SundayReady\updates\` |

State and logs are not next to the exe because a locked-down booth PC often cannot write
there. Checked state clears on a new calendar day; logs persist, one file per day per station.

## Configuration

### `station.json`

```jsonc
{
  "station": "Livestream Video",
  "operator": "J. Mercer",
  "checklists": ["livestream-video.json", "livestream-audio.json"],
  "service": { "doorsAt": "10:15", "streamAt": "10:25", "startsAt": "10:30", "venue": "SANCTUARY" },
  "quickLaunch": [{ "label": "vMix", "action": { "run": "C:\\Program Files (x86)\\vMix\\vMix64.exe" } }],
  "updates": { "enabled": true }
}
```

Absent, the app uses this PC's hostname and loads every checklist whose `station` matches it.

### A checklist file

One file per tab. Comments and trailing commas are allowed.

```jsonc
{
  "station": "Livestream Video",
  "name": "Video",                        // the tab label
  "items": [
    { "label": "Lens caps off", "type": "manual", "section": "Cameras & capture" },

    { "label": "Launch vMix", "type": "action", "section": "Cameras & capture",
      "action": { "run": "C:\\...\\vMix64.exe", "args": "\"C:\\vMix\\Sunday.vmix\"" },
      "verify": { "kind": "httpContains", "url": "http://127.0.0.1:8088/api", "contains": "<vmix>" } },

    { "label": "Cam 3 present", "type": "verified", "section": "Cameras & capture",
      "verify": { "kind": "httpContains", "url": "http://127.0.0.1:8088/api",
                  "contains": "Cam 3", "maxAttempts": 3 },
      "checkSteps": [                     // your words, shown on the failed-verify screen
        "Is the balcony camera's PoE injector lit?",
        "In vMix, does Add Input → NDI list \"BALCONY-CAM\"?"
      ],
      "remediationLabel": "Reload preset",
      "remediation": { "run": "C:\\vMix\\Presets\\Sunday.vmix" } }
  ]
}
```

**Item types.** `manual` is a checkbox. `action` is a button that launches something — add
`"label"` to rename it, and `"also"` to launch several things at once. `verified` is checked
by the app alone. An `action` with a `verify` block launches, then ticks itself when the
verifier agrees.

**Verifiers.**

| `kind` | Fields | Passes when |
|---|---|---|
| `processRunning` | `processName` | A process with that name is running |
| `httpContains` | `url`, `contains` | GET returns a body containing the string |
| `fileExists` | `path` | Path exists (environment variables are expanded) |
| `internetReachable` | `host` (optional) | Host answers ICMP, or TCP/443 if ping is blocked |
| `audioDevicePresent` | `nameContains` | **Not implemented yet** — always fails |

`maxAttempts` (default 10) is how many failed polls to absorb as "still starting" before the
item goes red. Polling runs every 5 seconds. A verifier that was passing and starts failing
un-ticks its item and logs the transition — the app will not leave a green tick on something
that stopped being true.

An unrecognised `kind` degrades that one item and logs a warning; the file still loads.

## Releases and updating

Cutting a release is pushing a tag:

```bash
git tag v0.5.0 && git push origin v0.5.0
```

That builds a self-contained win-x64 exe stamped with the tag and publishes it. Tags must be
`vMAJOR.MINOR.PATCH` — the updater compares them as versions.

Stations check on startup, download a newer release in the background, and stage it. The swap
happens at the **next** launch, before the window opens, so nothing changes under an operator
mid-service. Settings → Updates shows the current version and what is staged, and can check on
demand. Turn it off with `"updates": { "enabled": false }`.

The updater only ever replaces the exe — checklists and `station.json` are never touched.

## Building

```bash
dotnet run --project src/SundayReady
```

Avalonia · C# · .NET 9 · MVVM (CommunityToolkit.Mvvm) · win-x64.

[releases]: https://github.com/bowlsbeyk/SundayReady/releases/latest
