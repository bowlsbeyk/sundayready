# SundayReady

A boot-time preflight checklist for the A/V stations at Trinity Baptist Church.

Every Sunday an operator sits down and has to get a set of things right before the service
starts. SundayReady turns that tribal knowledge into a checklist that runs at logon — some
items the operator ticks, some launch software, and some the app verifies for itself by
checking whether a process is running or an API answers.

**One binary runs on every station.** What differs between stations is JSON, not code.
Adding a station is a config file, never a fork.

## Setting up a booth PC

> **Windows will warn you the first time.** The exe is not code-signed, so a file downloaded
> from GitHub carries the mark of the web and SmartScreen shows *"Windows protected your PC"*.
> Click **More info → Run anyway**. To avoid it entirely, right-click the downloaded **zip** →
> **Properties** → tick **Unblock** → OK, *then* extract. Some antivirus also takes an interest
> in a 47 MB unsigned executable; allow it if prompted. Signing the releases would remove this,
> but it needs a paid certificate.

1. Download `SundayReady-win-x64.zip` from the [latest release][releases] and unzip it
   somewhere **writable** — `%LOCALAPPDATA%\Programs\SundayReady` is a good choice.
   Do not use `C:\Program Files`: the app updates itself in place and cannot write there.
2. Run it. With no `station.json` it names itself after the PC's hostname and opens empty.
3. **Settings** → set the station name, operator, service times and quick-launch tiles.
4. **Edit** → build this station's checklists. Tick the ones this PC should show as tabs.
5. **Settings → Start at logon** → registers the Task Scheduler task.

That's the whole setup — no JSON editing required, though the files stay plain JSON if you
prefer them (see below). Nothing needs the .NET runtime installed; releases are self-contained.

The logon task waits 30 seconds and restarts on failure. The delay matters: `shell:startup`
races the software the verifiers check for, so checks would fail before vMix was listening.

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

Everything below can be set in the app — **Settings** for the station, **Edit** for the
checklists. The formats are documented because the files remain readable, diffable JSON that
you can edit by hand or copy between stations. The app watches the folder, so a file saved
from anywhere shows up immediately without a restart.

One caveat: files written by the editor are regenerated, so **hand-written comments are lost**
once you save that file in the app.

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
| `hostReachable` | `host`, `port` (optional) | A device answers — ping with no port, TCP connect with one |
| `audioDevicePresent` | `nameContains` | **Not implemented yet** — always fails |

**Checking cameras and other devices.** `hostReachable` is the one for gear on the network.
With no port it pings; with a port it connects, which is the stronger statement — a camera
that answers on 80 has its web UI up, not just an IP. Be clear on what it proves though: the
device is powered and on the network. It says nothing about where it is pointed, whether the
lens cap is off, or whether the feed reaches the switcher. For *that*, ask the switcher —
`httpContains` against the vMix API looking for the input's name is what proves a camera is
actually usable. Many stations want both: `hostReachable` tells you *which* thing broke,
`httpContains` tells you the shot is not there.

> Devices addressed by IP are a hostage to DHCP. If a camera's lease moves, the check fails
> and the camera is fine. Use a DNS or mDNS name (`cam3.local`) where you can, or give the
> gear static addresses — then the IP in the checklist stays true.

`maxAttempts` (default 10) is how many failed polls to absorb as "still starting" before the
item goes red. Polling runs every 5 seconds. A verifier that was passing and starts failing
un-ticks its item and logs the transition — the app will not leave a green tick on something
that stopped being true.

An unrecognised `kind` degrades that one item and logs a warning; the file still loads.

## The techdesk

One screen showing every station. Same binary — a PC becomes the techdesk by data, not by a
different download.

- **To look at it from any station:** press **TECHDESK** in the top bar. It opens as a window.
- **To make a PC the techdesk permanently:** Settings → Techdesk mode → tick "Run as techdesk
  instead", then restart. It boots straight into the board and loads no checklists of its own.
- **Pick the layout** in Settings: station columns, or the wall board that reduces to
  exceptions and is readable across the room.
- **To get back:** a techdesk PC shows no station screen, so both layouts carry a
  **SETTINGS** button. Untick techdesk mode there and restart to return to a station.

Stations publish a heartbeat every 15 seconds. Point every PC — stations *and* techdesk — at
one folder they can all reach:

```jsonc
"techdeskShare": "\\\\CHURCH-NAS\\AV\\sundayready"
```

Leave it unset and it falls back to a local folder, which only ever shows that one PC. A
station silent for 22 minutes (configurable with `techdeskHeartbeatMinutes`) shows as not
staffed, with buttons to page the volunteer or mark it not staffed for the day.

## Live viewer counts

Audience figures are techdesk telemetry. A failed fetch never affects whether a station reads
as ready — the tile just shows an em-dash.

**YouTube** works with an ordinary API key:

1. Google Cloud Console → new project → **APIs & Services → Library** → enable
   **YouTube Data API v3**.
2. **Credentials → Create credentials → API key.** Restrict it to the YouTube Data API.
3. Paste it into **Settings → Viewer counts**, along with the church's channel id (`UC…`).
   Press **Test now** — it reports the current count, or says why it can't.

Quota is 10,000 units a day, free. Reading a count costs 1 unit; finding *which* broadcast is
live costs 100, so that lookup happens once per session rather than per poll. Pinning a
specific broadcast id or URL skips it entirely.

**Facebook** works too, and does **not** need Meta App Review — that only applies to apps
reading data you don't own. An app left in **Development mode** can request permissions from
anyone with a role on it, and the church's own admin has a role on the church's own Page.

Getting a token that never expires, once:

1. [developers.facebook.com](https://developers.facebook.com) → **My Apps → Create app**.
   Leave it in **Development** mode — do not submit it for review.
2. **Graph API Explorer**, select that app → **Get User Access Token** → tick
   `pages_read_engagement` and `pages_show_list` → Generate.
3. That token is short-lived. Exchange it:
   `GET /oauth/access_token?grant_type=fb_exchange_token&client_id={app-id}&client_secret={app-secret}&fb_exchange_token={short-lived-token}`
4. With the long-lived user token: `GET /me/accounts` — find the church's Page and copy its
   `id` and its `access_token`. **A Page token derived from a long-lived user token has no
   expiry**; it dies only if that person changes their password or loses their Page role.
5. Settings → Viewer counts → paste the Page id and token → **Save token** → **Test Facebook**.

The token is stored **encrypted with Windows DPAPI for that user**, in
`%LOCALAPPDATA%\SundayReady\`, deliberately *not* in `station.json` — so copying a station's
config between PCs never carries its credentials along. "Forget token" removes it.

If Facebook counts stop arriving one day with an error mentioning the API version, bump
`GraphVersion` in `ViewerCountService`; Graph versions age out after about two years.

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

[releases]: https://github.com/bowlsbeyk/sundayready/releases/latest
[fb-live]: https://developers.facebook.com/docs/live-video-api/
