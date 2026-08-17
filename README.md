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
2. Run it. **A machine that has never run SundayReady opens a setup walkthrough** — six short
   screens that name the station, take your service times, and pick or create its first
   checklist, ending with a station that works.

That's the whole setup. The walkthrough writes an ordinary `station.json` and ordinary checklist
files; there is no special mode, and everything it sets is in **Settings** and **Edit**
afterwards. It can be skipped from any screen, and run again from **Settings → Identity → Run
walkthrough** — useful for a station being repurposed. Re-running it never overwrites a checklist
you already have: a name that collides gets a numbered suffix.

Nothing needs the .NET runtime installed; releases are self-contained.

**Every item in a new checklist is a plain tick-box, on purpose.** An item that launches vMix
needs a path that is right for your building, and one that checks a camera needs its address.
Shipped as guesses they would go red within seconds on a machine where none of that is configured
— and someone opening the app for the first time cannot tell that apart from a broken app. So you
start with a list that is honestly correct, then upgrade the items worth automating in the editor.

The logon task waits 30 seconds and restarts on failure. The delay matters: `shell:startup`
races the software the verifiers check for, so checks would fail before vMix was listening.

## Where things live

On Windows, where `DATA` is `%LOCALAPPDATA%\SundayReady`:

| What | Where |
|---|---|
| Checklists, `station.json` | Next to the exe. Edit freely; no rebuild needed. |
| Today's checked state | `DATA\state.json` |
| Completion logs | `DATA\logs\<date>_<station>.log` |
| Staged updates | `DATA\updates\` |

On macOS, where `DATA` is `~/Library/Application Support/SundayReady`:

| What | Where |
|---|---|
| Checklists, `station.json` | `DATA/checklists/`, `DATA/station.json` — **not** in the app bundle |
| Today's checked state | `DATA/state.json` |
| Completion logs | `DATA/logs/<date>_<station>.log` |
| Staged updates | `DATA/updates/` |

State and logs are never next to the executable, because a locked-down booth PC often cannot
write there. Logs persist, one file per day per station.

The one real difference is the checklists. On Windows an update replaces only the exe, so
content beside it is safe. On macOS the unit an update replaces is the whole `.app`, so content
inside the bundle would not survive one — it lives in `DATA` instead, seeded from the shipped
samples the first time the app runs.

**When the checklist starts again.** Settings → Service times, three options:

- **Every time SundayReady starts** *(default)* — the reliable one. If the app is opening, the
  list is fresh. An update installing, or the logon task restarting a crashed app, also counts
  as a start.
- **Only when the PC has been off and on** — keeps ticks through an app restart or an update.
  Be careful with this one: **sleep is not a power cycle**, and with Windows **Fast Startup**
  neither is *Shut down* on many machines — the kernel session is restored, so boot time and
  uptime do not change. If your PCs sleep rather than shut down, this will look like it is
  doing nothing at all.
- **Only at midnight.**

Whichever you pick, the **service times** above also start the list again at each changeover,
and a new calendar day always clears it.

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
  "service": {
    "doorsAt": "10:15", "streamAt": "10:25", "venue": "SANCTUARY",
    "starts": ["09:00", "11:00"],   // every service that day; the countdown targets the next
    "resetLeadMinutes": 90          // preparation for a service opens this long before it
  },
  "quickLaunch": [{ "label": "vMix", "action": { "run": "C:\\Program Files (x86)\\vMix\\vMix64.exe" } }],
  "updates": { "enabled": true, "channel": "production" },
  "configured": true              // written by the app; see below
}
```

Absent, the app uses this PC's hostname and loads every checklist whose `station` matches it.
The same fallback applies to a file that will not parse, so a booth PC guesses rather than
opening into an error.

`configured` is written by the app whenever it saves this file, and is not something to set by
hand. It marks the difference between "somebody set this station up" and "this file is a stub" —
without it, a station whose operator deliberately chose *no* checklists had its whole config
thrown away and hostname auto-detect used instead.

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

**A shutdown checklist** is just another file with `"countsTowardReady": false`. It shows as a
tab and works like any other list, but it is left out of the **Ready to go** gate — otherwise a
station could never be ready before the service it is getting ready for, because the packing-up
items would still be open. There is a tick-box for it in the editor.

**In-app help.** Press **HELP** in the top bar (or in the checklist editor). It explains the
three kinds of item, what every verifier does with an example and its gotcha, and the handful
of ideas worth knowing — the gate, overrides, retry budgets, when the list starts again. The
editor also shows a short version of the same text next to the verifier you are choosing.

**Item types.** `manual` is a checkbox. `action` is a button that launches something — add
`"label"` to rename it, and `"also"` to launch several things at once. `verified` is checked
by the app alone. An `action` with a `verify` block launches, then ticks itself when the
verifier agrees.

**Instructions and sub-steps.** An item can carry either, reached from a chip on its row:

```jsonc
{ "label": "Set up the stream in Subsplash", "type": "manual",
  "instructions": [                       // read-only, opens on a HOW? chip
    "Open the Subsplash dashboard and sign in.",
    "Media → Live → New broadcast." ],
  "subSteps": [                           // ticked individually, shown as 2/4 on the row
    "Broadcast created in Subsplash",
    "Stream key pasted into vMix" ] }
```

`instructions` are guidance and affect nothing. `subSteps` are remembered with the rest of the
day, and ticking the last one ticks the item — though the item can still be ticked directly by
someone who knows the routine. Neither is `checkSteps`, which appears only when a verifier has
*failed*: that is diagnosis, these are the work.

## Installing on a Mac

Grab the zip for the machine from [Releases](https://github.com/bowlsbeyk/sundayready/releases) —
`SundayReady-osx-arm64.zip` for Apple Silicon, `SundayReady-osx-x64.zip` for Intel — unzip it and
drag `SundayReady.app` into Applications.

The first launch needs one extra step. These builds are ad-hoc signed but **not notarized**, which
takes a paid Apple Developer account, so macOS blocks the first open:

> "SundayReady" cannot be opened because Apple cannot check it for malicious software.

Clear it once, either way:

- **System Settings → Privacy & Security**, scroll to the bottom, and press **Open Anyway** next to
  the message about SundayReady. (On macOS 15 and later this is the only route — right-click → Open
  no longer works.)
- or in Terminal:

  ```bash
  xattr -dr com.apple.quarantine /Applications/SundayReady.app
  ```

That is once per machine, not once per update: updates the app downloads itself are never
quarantined, so in-app updating never hits this again.

**Where a Mac keeps its checklists.** Not inside the bundle. An update on macOS replaces the whole
`.app`, so anything in there would be destroyed by it — the checklists and `station.json` live in
`~/Library/Application Support/SundayReady/` instead, seeded from the samples on first run. On
Windows they stay next to the exe, because there an update replaces only the exe.

**What does not work on a Mac.** Nothing platform-specific is silently broken, but two verifier
kinds are aimed at Windows software: `processRunning` looks for a process by name, which works but
wants a Mac process name, and vMix does not exist on macOS at all. Quick-launch tiles take `.app`
paths and URLs. Start-at-login uses a LaunchAgent, and API tokens go into the login Keychain
instead of DPAPI.

## Release channels

A tag is a release, and the tag's suffix is the channel:

| Tag | Channel | Who takes it |
|---|---|---|
| `v1.2.3` | production | every station |
| `v1.2.3-beta.1` | beta | stations set to beta, alpha or dev |
| `v1.2.3-alpha.1` | alpha | alpha and dev |
| `v1.2.3-dev.4` | dev | dev only |

A channel is a risk tolerance, not a branch — there is one history of releases and a station's
channel says how early in it that station is happy to pick them up. Lower channels take everything
above them too, so a station on beta still gets production releases; it just sees the betas first.

Set it per station in **Settings → Updates**, or as `updates.channel` in `station.json`. It defaults
to production, so a station only ever leaves it deliberately.

Cutting one:

```bash
git tag v0.15.0-beta.1 && git push origin v0.15.0-beta.1
```

The workflow builds `win-x64`, `osx-arm64` and `osx-x64`, marks anything with a suffix as a GitHub
prerelease, and publishes the assets with a `SHA256SUMS.txt`. Nothing else changes — the suffix is
the whole mechanism, and the app reads it back out of its own `InformationalVersion`.

**Getting a station onto a channel.** Channels need code that only exists from **0.15.0** onward.
A station on 0.14.0 or earlier ships an updater that asks GitHub only for the latest *finished*
release, so it cannot see a prerelease no matter what its config says. Those stations will pick up
the next production release automatically, and can follow a channel from then on. To put a station
on beta sooner, install the build by hand from the releases page once.

**How the app finds an update**, since it is not the obvious way. A production station asks
`/releases/latest`: one request, exact, assets included. Prerelease channels cannot use that
endpoint — it excludes prereleases by design — and the `/releases` collection endpoint turned out
to be unusable, returning `200 []` for this repository for long stretches while the releases were
plainly there. So prereleases are discovered from the tag list and resolved through
`/releases/tags/…`, newest first, stepping over tags that have no release behind them. That last
part is not defensive programming for its own sake: a tag whose build failed is an ordinary event,
and there are several in this repository's history.

**Verifiers.**

| `kind` | Fields | Passes when |
|---|---|---|
| `processRunning` | `processName` | A process with that name is running |
| `httpContains` | `url`, `contains` | GET returns a body containing the string |
| `fileExists` | `path` | Path exists (environment variables are expanded) |
| `internetReachable` | `host` (optional) | Host answers ICMP, or TCP/443 if ping is blocked |
| `hostReachable` | `host`, `port` (optional) | A device answers — ping with no port, TCP connect with one |
| `ndiSourcePresent` | `nameContains` | An NDI source with that text in its name is on the network |
| `audioDevicePresent` | `nameContains` | **Not implemented yet** — always fails |

**Checking cameras and other devices.** `hostReachable` is the one for gear on the network.
With no port it pings; with a port it connects, which is the stronger statement — a camera
that answers on 80 has its web UI up, not just an IP. Be clear on what it proves though: the
device is powered and on the network. It says nothing about where it is pointed, whether the
lens cap is off, or whether the feed reaches the switcher. For *that*, ask the switcher —
`httpContains` against the vMix API looking for the input's name is what proves a camera is
actually usable. Many stations want both: `hostReachable` tells you *which* thing broke,
`httpContains` tells you the shot is not there.

**NDI sources.** `ndiSourcePresent` asks the network which NDI senders are announcing
themselves over mDNS — the same list your switcher shows under *Add Input → NDI* — and passes
when one of them contains your text. No NDI runtime to install. It proves the sender is
powered, on the network and advertising; it does not prove the switcher has taken it as an
input. When it fails it lists the sources it *did* find, which usually reveals a name that has
changed. It cannot see across subnets unless you run an NDI Discovery Server.

> Devices addressed by IP are a hostage to DHCP. If a camera's lease moves, the check fails
> and the camera is fine. Use a DNS or mDNS name (`cam3.local`) where you can, or give the
> gear static addresses — then the IP in the checklist stays true.

`maxAttempts` (default 10) is how many failed polls to absorb as "still starting" before the
item goes red. Polling runs every 5 seconds. A verifier that was passing and starts failing
un-ticks its item and logs the transition — the app will not leave a green tick on something
that stopped being true.

An unrecognised `kind` degrades that one item and logs a warning; the file still loads.

## A Sunday, end to end

**Setup.** The checklist is the screen. Work down it; verified items tick themselves.

**Ready to go** unlocks once every gating item is ticked or overridden. Pressing it is the
operator saying setup is done — it goes in the completion log, and the checklist recedes.

**During the service** the screen becomes a count-up into the service with the clock beneath
it, the live YouTube and Facebook counts, and a panel naming anything that has *stopped*
passing since you went ready. The verifiers never stop running — while you are live, what
matters is not the list but whether something that was true has quietly stopped being true.
*Show the checklist* brings the list back whenever you want it.

**Service finished** is a button, not a guess: services run long and short, and only the person
in the room knows. It opens whichever checklist has **"Open this checklist after the service"**
ticked in the editor — tick it on your post-show list. If nothing is nominated it falls back to
the first list that sits outside the gate, and if there is no such list it tells you so rather
than appearing to do nothing.

If you have a second service that day, the changeover (see service times) puts the station back
into setup with a fresh list on its own.

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
