# SundayReady

**A preflight checklist for church A/V booths.** It opens itself before the service and makes sure
the same things get done every week, whoever is sitting there.

Every Sunday somebody sits down at a sound desk, a streaming PC or a presentation machine and has to
get a set of things right before the service starts. Most of that knowledge lives in one or two
people's heads, and it walks out of the building when they go on holiday. SundayReady turns it into a
checklist that runs at logon — some items a person ticks, some launch software, and some the app
verifies for itself by checking whether a process is running, a device answers, or an API returns
what it should.

**One binary runs on every station.** What differs between stations — and between churches — is
JSON, not code. Adding a station is a config file, never a fork. The same binary is also the
techdesk screen that watches every station at once.

Windows and macOS, self-contained. Nothing to install alongside it, no server, no account, no
network service. A church with one laptop can use it as sensibly as one with six booths.

## Is this for you?

Probably, if any of these are familiar:

- More than one person runs the booth, and they each do it slightly differently.
- Something gets forgotten a few times a year — the recording, the stream title, a camera left on
  the wrong preset — and nobody notices until afterwards.
- Training a new volunteer means standing behind them for a month.
- You would like to know, from across the room, whether the sound desk is actually ready.

**It assumes nothing about your gear.** The checks are deliberately generic — is this program
running, does this URL say what it should, is this device answering on the network, is this NDI
source being advertised. That covers most booths without SundayReady needing to know what any of your
equipment is. Nothing here is affiliated with or endorsed by any of the software it can be pointed at.

> **Where it came from, honestly.** It was written for the A/V team at one church, Trinity Baptist,
> and the sample checklists still smell of it — vMix, ProPresenter, a Subsplash stream.
> **None of that is baked in.** The samples are examples to delete or rewrite; see
> [making it yours](#making-it-yours). It is early software, used every week at the church it was
> built for and not yet by many others, so expect to find rough edges and please say so when you do.

## Contents

- [Is this for you?](#is-this-for-you)
- [Install](#install) · [Windows](#windows) · [macOS](#macos)
- [The first five minutes](#the-first-five-minutes) — walkthrough, guided tour, help
- [Making it yours](#making-it-yours) — starting from a blank booth
- [A Sunday, end to end](#a-sunday-end-to-end)
- [Writing checklists](#writing-checklists) — [item types](#item-types),
  [instructions and steps](#instructions-and-steps-to-tick-off), [verifiers](#verifiers),
  [cameras and devices](#checking-cameras-and-other-devices), [NDI](#ndi-sources)
- [`station.json`](#stationjson) · [when the list starts again](#when-the-checklist-starts-again)
- [Where things live](#where-things-live)
- [The system map](#the-system-map) *(branch)*
- [The techdesk](#the-techdesk)
- [Live viewer counts](#live-viewer-counts) — [YouTube](#youtube), [Facebook](#facebook)
- [Updating](#updating) · [release channels](#release-channels) · [cutting a release](#cutting-a-release)
- [Building, and contributing](#building-and-contributing)
- [Licence](#licence)

---

## Install

Grab the build for the machine from the [latest release][releases]. Every release carries
`SHA256SUMS.txt` if you want to check what you downloaded.

| File | For |
|---|---|
| `SundayReady-win-x64.zip` | Windows — **use this to install** |
| `SundayReady-win-x64.exe` | Windows — bare exe, what the in-app updater fetches |
| `SundayReady-osx-arm64.zip` | macOS on Apple Silicon (M1 and later) |
| `SundayReady-osx-x64.zip` | macOS on Intel |

### Windows

> **Windows will warn you the first time.** The exe is not code-signed, so a file downloaded from
> GitHub carries the mark of the web and SmartScreen shows *"Windows protected your PC"*. Click
> **More info → Run anyway**. To avoid it entirely, right-click the downloaded **zip** →
> **Properties** → tick **Unblock** → OK, *then* extract. Some antivirus also takes an interest in a
> 47 MB unsigned executable; allow it if prompted. Signing would remove this, but it needs a paid
> certificate.

1. Unzip `SundayReady-win-x64.zip` somewhere **writable** — `%LOCALAPPDATA%\Programs\SundayReady`
   is a good choice. **Not** `C:\Program Files`: the app updates itself in place and cannot write
   there.
2. Run it. A machine that has never run SundayReady opens the
   [setup walkthrough](#the-first-five-minutes).

### macOS

1. Unzip and drag `SundayReady.app` into **Applications**.
2. The first launch needs one extra step. These builds are ad-hoc signed but **not notarized** —
   that needs a paid Apple Developer account — so macOS blocks the first open:

   > "SundayReady" cannot be opened because Apple cannot check it for malicious software.

   Clear it once, either way:

   - **System Settings → Privacy & Security**, scroll to the bottom, press **Open Anyway** next to
     the message about SundayReady. On macOS 15 and later this is the only route — right-click →
     **Open** no longer works.
   - or, in Terminal:

     ```bash
     xattr -dr com.apple.quarantine /Applications/SundayReady.app
     ```

   That is **once per machine, not once per update**. Updates the app downloads itself are never
   quarantined, so in-app updating never hits this again.

**What differs on a Mac.** Nothing is silently broken, but be aware:

- Checklists live **outside** the app bundle — see [Where things live](#where-things-live).
- Start-at-login uses a LaunchAgent instead of Task Scheduler.
- API tokens go into the login **Keychain** instead of Windows DPAPI.
- `processRunning` works but wants a *Mac* process name, and vMix does not exist on macOS at all.
- Quick-launch tiles take `.app` paths and URLs.

### Start it at logon

**Settings → Start at logon.** The task waits 30 seconds before opening the app and restarts it on
failure. The delay matters: the Windows startup folder races the software the verifiers check for,
so checks would fail before vMix was even listening.

---

## The first five minutes

**The setup walkthrough** opens by itself on a machine that has never run SundayReady. Six short
screens: name the station, service times and when the list starts again, pick or create a first
checklist, start-at-logon. It writes an ordinary `station.json` and ordinary checklist files —
there is no special mode, and everything it sets is reachable in **Settings** and **Edit**
afterwards.

Skippable from any screen, and re-runnable from **Settings → Identity → Run walkthrough**, which is
the quickest way to set up a station being repurposed. Re-running it never overwrites a checklist
you already have: a name that collides gets a numbered suffix.

**Every item in a new checklist is a plain tick-box, on purpose.** An item that launches vMix needs
a path that is right for your building, and one that checks a camera needs its address. Shipped as
guesses they would go red within seconds on a machine where none of that is configured — and
someone opening the app for the first time cannot tell that apart from a broken app. So you start
with a list that is honestly correct, then upgrade the items worth automating.

**The guided tour** is the other half. The walkthrough configures a station without ever showing you
the app; the tour shows you the app without changing anything. It dims the window and spotlights one
real control at a time — the tabs, the list, the **Ready to go** gate — then has you open the editor,
add an item and save it *yourself*, because being shown where a button is and pressing it are
different amounts of learning. Ten stops, **Skip tour** pinned at the top throughout, and it ends by
having you open HELP so you know where it is.

Offered at the end of the walkthrough ("Show me around"), and always available from the same button
at the top of **HELP**.

**In-app help.** **HELP** is in the top bar of every screen — the checklist and the editor, the same
window from both. It covers the three kinds of item, every verifier with an example and its gotcha,
and twenty topics grouped by when you need them: *on a Sunday*, *when something will not pass*,
*setting a station up*, *keeping it up to date*.

It has a **search box**, which matters more than the contents. Somebody opens that window because
something is wrong with ninety seconds to go, not to read a manual — so `red`, `override`, `vmix`
or `start again` go straight to the answer, and entries whose title matches rank first. The editor
shows a short version of the same text beside the verifier you are choosing.

---

## Making it yours

A fresh install ships with sample checklists from the church this was written for. They are there so
the app is not an empty box on first launch — **they are examples, not a starting configuration.**
Nothing in the app depends on them.

The honest way to start is to throw them away and write down what your booth actually does:

1. **Watch someone do it.** Sit behind your most experienced volunteer one Sunday and write down
   every single thing they touch before the service, in order. That list is your first checklist,
   and it will be better than anything you could design at a desk.
2. **Delete the samples.** **Edit → Delete** on each, or just untick them so they stop being tabs.
3. **Type your list in as `manual` items** — plain tick-boxes, no automation. Get the words right
   first. Write them the way you would say them out loud: *"Lens caps off"*, not *"Verify optical
   apertures unobstructed"*.
4. **Then automate the ones worth automating.** An item is worth a verifier when it is something the
   computer can see and a tired person can miss — a program not running, a camera off the network, a
   recording drive that is full. Leave the rest as tick-boxes. A checklist of twelve honest
   tick-boxes beats one of four clever checks and eight forgotten steps.
5. **Write the `checkSteps`.** When a check fails at 10:25, the difference between a useful app and
   an irritating one is whether it tells the volunteer what to go and look at. This is the part only
   you can write.

One station per machine, one tab per checklist. A church with a sound desk, a streaming PC and a
presentation machine has three stations and can point them all at [a techdesk](#the-techdesk).

> The sample files are `livestream-video`, `livestream-audio`, `go-live`, `post-show` and `shutdown`
> — a reasonable shape for a streaming church even if every line in them is wrong for you. Copy the
> shape, replace the contents.

---

## A Sunday, end to end

**Setup.** The checklist is the screen. Work down it; verified items tick themselves. A verified
item that fails turns red and offers the reason, a **Retry now**, and — if you are out of time —
**Override & note**, which asks for initials and a typed reason and records the service as partial.
It is an honest escape hatch, not a way to silence a check.

**Ready to go** unlocks once every gating item on *every* tab is ticked or overridden. Pressing it
is the operator saying setup is done; it goes in the completion log and the checklist recedes.

**During the service** the screen becomes a count-up into the service with the clock beneath it, the
live YouTube and Facebook counts, and a panel naming anything that has *stopped* passing since you
went ready. The verifiers never stop running — while you are live, what matters is not the list but
whether something that was true has quietly stopped being true. **Show the checklist** brings the
list back whenever you want it.

**Service finished** is a button, not a guess: services run long and short, and only the person in
the room knows. It opens whichever checklist has **"Open this checklist after the service"** ticked
in the editor — tick that on your post-show list. With nothing nominated it falls back to the first
list outside the gate, and if there is no such list it says so rather than appearing to do nothing.

If there is a second service that day, the changeover puts the station back into setup with a fresh
list on its own. See [when the checklist starts again](#when-the-checklist-starts-again).

Everything is written to a completion log — one file per day per station, append-only: what was
ticked, what ticked itself, anything overridden and why, and who signed off. **LOG** in the top bar
opens it. It exists so "what happened last week" is a question with an answer.

---

## Writing checklists

Use **Edit** in the top bar. The formats below are documented because the files stay readable,
diffable JSON you can hand-edit or copy between stations — the app watches the folder, so a file
saved from anywhere shows up without a restart.

> Files written by the editor are regenerated, so **hand-written comments are lost** once you save
> that file in the app.

One file is one tab. Comments and trailing commas are allowed when reading.

```jsonc
{
  "station": "Livestream Video",
  "name": "Video",                        // the tab label
  "countsTowardReady": true,              // default; false keeps it out of the Ready gate
  "openAfterService": false,              // "Service finished" opens this one
  "items": [
    { "label": "Lens caps off", "type": "manual", "section": "Cameras & capture" },

    { "label": "Launch vMix", "type": "action", "section": "Cameras & capture",
      "action": { "run": "C:\\...\\vMix64.exe", "args": "\"C:\\vMix\\Sunday.vmix\"" },
      "verify": { "kind": "httpContains", "url": "http://127.0.0.1:8088/api", "contains": "<vmix>" } },

    { "label": "Cam 3 present", "type": "verified", "section": "Cameras & capture",
      "verify": { "kind": "httpContains", "url": "http://127.0.0.1:8088/api",
                  "contains": "Cam 3", "maxAttempts": 3 },
      "checkSteps": [                     // your words, shown on the failed-verify screen
        "Is Cam 3's PoE injector lit? It is the grey box on the shelf behind the booth.",
        "In vMix, does Add Input → NDI list the camera?"
      ],
      "remediationLabel": "Reload preset",
      "remediation": { "run": "C:\\vMix\\Presets\\Sunday.vmix" } }
  ]
}
```

### Item types

| `type` | What it is |
|---|---|
| `manual` | A tick-box. Somebody confirms it. |
| `action` | A button that launches something. `"label"` renames the button; `"also"` launches several things at once. |
| `verified` | Checked by the app alone, and ticks itself. |

An `action` with a `verify` block launches, then ticks itself when the verifier agrees.

**Sections** are a heading above a run of items — type the same section name on consecutive items
and they group under one divider. Purely for reading.

**A shutdown or post-show checklist** is just another file with `"countsTowardReady": false`. It
shows as a tab and behaves like any other list, but stays out of the **Ready to go** gate —
otherwise a station could never be ready before the service it is getting ready for, because the
packing-up items would still be open. There is a tick-box for it in the editor.

### Instructions and steps to tick off

An item can carry either or both, reached from a chip on its row:

```jsonc
{ "label": "Set up the stream in Subsplash", "type": "manual",
  "instructions": [                       // read-only, opens on a HOW? chip
    "Open the Subsplash dashboard and sign in.",
    "Media → Live → New broadcast." ],
  "subSteps": [                           // ticked individually, shown as 2/4 on the row
    "Broadcast created in Subsplash",
    "Stream key pasted into vMix" ] }
```

`instructions` are guidance and affect nothing — for the job someone does four times a year and
cannot be expected to remember. `subSteps` are remembered with the rest of the day, and ticking the
last one ticks the item, though the item can still be ticked directly by someone who knows the
routine.

Neither is `checkSteps`, which appears only when a verifier has **failed**. That is diagnosis; these
are the work. `checkSteps` is the most valuable thing in a checklist and the app cannot write it for
you: it is what you would tell a volunteer over the phone.

### Verifiers

| `kind` | Fields | Passes when |
|---|---|---|
| `processRunning` | `processName` | A process with that name is running |
| `httpContains` | `url`, `contains` | GET returns a body containing the string |
| `fileExists` | `path` | Path exists (environment variables are expanded) |
| `internetReachable` | `host` (optional) | Host answers ICMP, or TCP/443 if ping is blocked |
| `hostReachable` | `host`, `port` (optional) | A device answers — ping with no port, TCP connect with one |
| `ndiSourcePresent` | `nameContains` | An NDI source with that text in its name is on the network |
| `audioDevicePresent` | `nameContains` | **Not implemented yet** — always fails |

`maxAttempts` (default 10) is how many failed polls to absorb as "still starting" before the item
goes red. Polling runs every 5 seconds. A verifier that was passing and starts failing **un-ticks**
its item and logs the transition — the app will not leave a green tick on something that stopped
being true.

An unrecognised `kind` degrades that one item and logs a warning; the file still loads.

### Checking cameras and other devices

`hostReachable` is the one for gear on the network. With no port it pings; with a port it connects,
which is the stronger statement — a camera answering on 80 has its web UI up, not just an IP.

Be clear on what it proves: the device is powered and on the network. It says nothing about where it
is pointed, whether the lens cap is off, or whether the feed reaches the switcher. For *that*, ask
the switcher — `httpContains` against the vMix API looking for the input's name is what proves a
camera is actually usable. Many stations want both: `hostReachable` tells you *which* thing broke,
`httpContains` tells you the shot is not there.

> Devices addressed by IP are a hostage to DHCP. If a camera's lease moves, the check fails and the
> camera is fine. Use a DNS or mDNS name (`cam3.local`) where you can, or give the gear static
> addresses — then the IP in the checklist stays true.

### NDI sources

`ndiSourcePresent` asks the network which NDI senders are announcing themselves over mDNS — the same
list your switcher shows under *Add Input → NDI* — and passes when one contains your text. No NDI
runtime to install.

It proves the sender is powered, on the network and advertising; it does not prove the switcher has
taken it as an input. When it fails it lists the sources it *did* find, which usually reveals a name
that has changed. It cannot see across subnets unless you run an NDI Discovery Server.

---

## `station.json`

Everything here can be set in **Settings**; the file is documented because it stays readable and
copyable.

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
  "resetMode": "everyLaunch",       // everyLaunch | powerCycle | daily
  "quickLaunch": [{ "label": "vMix", "action": { "run": "C:\\Program Files (x86)\\vMix\\vMix64.exe" } }],
  "techdeskShare": "\\\\CHURCH-NAS\\AV\\sundayready",
  "updates": { "enabled": true, "channel": "production" },
  "configured": true                // written by the app; see below
}
```

With no `station.json` at all, the app uses this PC's hostname and loads every checklist whose
`station` matches it. The same fallback applies to a file that will not parse, so a booth PC guesses
rather than opening into an error.

`configured` is written by the app whenever it saves this file and is not something to set by hand.
It marks the difference between "somebody set this station up" and "this file is a stub" — without
it, a station whose operator deliberately chose *no* checklists would have its whole config thrown
away and hostname auto-detect used instead.

**Quick launch** is the row of buttons at the bottom of the rail. They open the software this
station uses without hunting for it on the desktop; they are not checklist items and nothing depends
on them.

### When the checklist starts again

**Settings → Service times**, three options:

- **Every time SundayReady starts** *(default)* — the reliable one. If the app is opening, the list
  is fresh. An update installing, or the logon task restarting a crashed app, also counts as a start.
- **Only when the PC has been off and on** — keeps ticks through an app restart or an update. Be
  careful: **sleep is not a power cycle**, and with Windows **Fast Startup** neither is *Shut down*
  on many machines — the kernel session is restored, so boot time and uptime do not change. If your
  PCs sleep rather than shut down, this looks like it is doing nothing at all.
- **Only at midnight.**

Whichever you pick, **service times** also start the list again at each changeover — with services
at 09:00 and 11:00 and a 90-minute lead, the list goes fresh at 09:30 for the second one — and a new
calendar day always clears it.

---

## Where things live

State and logs are never beside the executable, because a locked-down booth machine often cannot
write to its own folder.

**Windows**, where `DATA` is `%LOCALAPPDATA%\SundayReady`:

| What | Where |
|---|---|
| Checklists, `station.json` | Next to the exe. Edit freely; no rebuild needed. |
| Today's checked state | `DATA\state.json` |
| Completion logs | `DATA\logs\<date>_<station>.log` |
| Staged updates | `DATA\updates\` |

**macOS**, where `DATA` is `~/Library/Application Support/SundayReady`:

| What | Where |
|---|---|
| Checklists, `station.json` | `DATA/checklists/`, `DATA/station.json` — **not** in the app bundle |
| Today's checked state | `DATA/state.json` |
| Completion logs | `DATA/logs/<date>_<station>.log` |
| Staged updates | `DATA/updates/` |

The one real difference is the checklists, and it follows from how updates work on each platform. On
Windows an update replaces only the exe, so content beside it is safe. On macOS the unit an update
replaces is the whole `.app`, so content inside the bundle would not survive one — it lives in `DATA`
instead, seeded from the shipped samples the first time the app runs.

---

## The system map

> On the `system-map` branch, not yet in a release.

**MAP** in the top bar opens a live diagram of the building: devices as boxes, the wires between
them drawn in the signal's colour — NDI blue, XLR gold, Dante teal — animated while the signal is
believed to be arriving. Both boxes **and wires** can carry the same verifiers as checklist items,
which is the point: a camera that is powered and a switcher that is running still tell you nothing
about whether the camera *reaches* the switcher. The one red pulsing wire is the thing that broke;
everything it starves goes faint and still, so the map answers "where" and not just "whether".

Maps are shared. They live in a `maps` folder beside the techdesk snapshots (`system-map.json`,
human-readable), so every machine sees the same building. With no share configured they fall back
to a local folder, like everything else.

**Three views of one map.** The switcher at the top gives you the same devices three ways, because
the question changes with the hour. **Signal flow** is what feeds what. **Building** groups the same
boxes by the room they live in, faults first — the answer to "where do I walk to", which a
signal-flow diagram is structurally bad at. **Stream path** is the chain out of the building,
hop by hop, with one sentence saying where it stops. Nothing is entered twice; a device's room and
its wires already say all of it.

**The workflow.** Open MAP → first run offers *create an empty map* or *start from a worked
example* — a real church rig with a stage box, wireless receivers, a digital snake and a console
carrying seven runs across seven named sockets, so you can see the shape before renaming it to your
own gear. **Edit layout** → **Add device**, then **drag a box's middle to move it and drag from
either end to wire it**. Click a wire to select it. **Delete** removes whatever is selected and
**Ctrl+Z** takes it back — including a deleted device's wires, all of them, together. Selecting
anything while editing opens its editor in the rail: label, the mono sub-line, kind, and a
**verifier** — same kinds as the checklist. Wires take a type, a label, a cable length and a
verifier of their own. **Apply changes**, then **Save map**.

**Runs that go both ways.** A snake carrying stage inputs up and IEM mixes back down is one cable,
not two, and ticking **carries both ways on one cable** draws it as one — flow drifting in both
directions at once. It is a topology claim too: a two-way run is treated as broken from either end,
because losing that cable really does cost you both directions.

**Ports.** A device can declare its sockets — `AES50 A`, `CH 25-26`, `MAIN L/R` — and a run can name
the one it lands on. The map then anchors wires to real sockets instead of spreading them evenly,
so the diagram says *the stage box arrives on AES50 A* rather than *something arrives somewhere on
the left*. Two runs sharing one socket stay one point and the tick widens, because two cables in one
jack is a fact worth seeing. Ports are entirely optional: skip them and a busy box still fans its
wires along the edge so each stays followable.

**Notes.** **Add note** pins one to the canvas, for the things boxes and lines cannot say — the run
that goes through the ceiling, the spare cable in the drawer under the amp, the XLR that needs a
wiggle. Select a box first and the note attaches to it and follows it around. Notes can be marked as
warnings, and that is as loud as they get: a note never affects whether the system reads green,
because the moment it could, notes start getting written to move the verdict.

Every device carries a **tier** saying how the app knows its state, and the map refuses to lie
about it: *verified* (we check it — the only tier that can hold Ready to go), *reported* (a
third-party API tells us; believed, badged, never blocking), *inferred* (drawn hollow — a guess
must never look checked), *ask a human* (somebody's job). Nothing off campus can ever block a
volunteer's checklist; that is enforced in the model, not the styling.

A box can link to another map — one tidy overview drilling into per-room detail — and a sub-map's
verified failures surface on the box that links to it.

**Double-click any box** (or select it → **Inspect**) for the device inspector: every connection in
and out with its state, type and cable length, plus the signal's onward path traced hop by hop with
a plain-English verdict — *"The break is at vMix. Everything after it is starved, not broken."*
Runs the map documents but nothing verifies are flagged **DOCUMENTED BUT NEVER VERIFIED**, with a
one-click **make this a checklist item** — which is how the checklist grows to match reality.

**+ New connection type** in the legend opens the type registry: add your own signal types with
curated colours (kept away from the built-in hues so the legend stays learnable), dash style, flow
speed and length warnings. Deleting a type in use reassigns its runs first — never orphans them.

Still to come from the design handoff: the floorplan view with cable routing, and the off-campus
stream path view with the property line.

## The techdesk

One screen showing every station. Same binary — a PC becomes the techdesk by data, not by a
different download.

- **To look at it from any station:** press **TECHDESK** in the top bar. It opens as a window.
- **To make a PC the techdesk permanently:** Settings → Techdesk mode → tick "Run as techdesk
  instead", then restart. It boots straight into the board and loads no checklists of its own.
- **Pick the layout** in Settings: station columns, or the wall board that reduces to exceptions and
  is readable across the room.
- **To get back:** a techdesk PC shows no station screen, so both layouts carry a **SETTINGS**
  button. Untick techdesk mode there and restart.

Stations publish a heartbeat every 15 seconds. Point every PC — stations *and* techdesk — at one
folder they can all reach:

```jsonc
"techdeskShare": "\\\\CHURCH-NAS\\AV\\sundayready"
```

Leave it unset and it falls back to a local folder, which only ever shows that one PC. A station
silent for 22 minutes (`techdeskHeartbeatMinutes`) shows as not staffed, with buttons to page the
volunteer or mark it not staffed for the day. What "page the volunteer" runs is `techdeskPage` — a
`tel:` or `sms:` link, a chat webhook, whatever the church actually uses.

---

## Live viewer counts

Audience figures are techdesk telemetry. A failed fetch never affects whether a station reads as
ready — the tile just shows an em-dash.

### YouTube

Works with an ordinary API key:

1. Google Cloud Console → new project → **APIs & Services → Library** → enable **YouTube Data API v3**.
2. **Credentials → Create credentials → API key.** Restrict it to the YouTube Data API.
3. Paste it into **Settings → Viewer counts**, with the church's channel id (`UC…`). Press
   **Test now** — it reports the current count, or says why it can't.

Quota is 10,000 units a day, free. Reading a count costs 1 unit; finding *which* broadcast is live
costs 100, so that lookup happens once per session rather than per poll. Pinning a specific
broadcast id or URL skips it entirely.

### Facebook

Works too, and does **not** need Meta App Review — that only applies to apps reading data you don't
own. An app left in **Development mode** can request permissions from anyone with a role on it, and
the church's own admin has a role on the church's own Page.

Getting a token that never expires, once:

1. [developers.facebook.com](https://developers.facebook.com) → **My Apps → Create app**. Leave it in
   **Development** mode — do not submit it for review.
2. **Graph API Explorer**, select that app → **Get User Access Token** → tick
   `pages_read_engagement` and `pages_show_list` → Generate.
3. That token is short-lived. Exchange it:
   `GET /oauth/access_token?grant_type=fb_exchange_token&client_id={app-id}&client_secret={app-secret}&fb_exchange_token={short-lived-token}`
4. With the long-lived user token: `GET /me/accounts` — find the church's Page and copy its `id` and
   its `access_token`. **A Page token derived from a long-lived user token has no expiry**; it dies
   only if that person changes their password or loses their Page role.
5. Settings → Viewer counts → paste the Page id and token → **Save token** → **Test Facebook**.

The token is stored encrypted by the operating system — **Windows DPAPI** for that user, or the
**login Keychain** on macOS — deliberately *not* in `station.json`, so copying a station's config
between machines never carries its credentials along. **Forget token** removes it.

If Facebook counts stop arriving one day with an error mentioning the API version, bump
`GraphVersion` in `ViewerCountService`; Graph versions age out after about two years.

---

## Updating

Stations check on startup, download a newer release in the background, and stage it. The swap
happens at the **next** launch, before the window opens, so nothing changes under an operator
mid-service.

When an update is staged, **Settings → Updates** also offers **Install and restart now** — safe
precisely because somebody asked for it. The app closes and comes back a couple of seconds later on
the new build.

Turn it all off with `"updates": { "enabled": false }`.

**A station's own content is never touched.** On Windows the updater replaces only the exe, so the
checklists and `station.json` beside it survive untouched. On macOS the whole `.app` is replaced,
which is exactly why a Mac keeps its content outside the bundle — see
[Where things live](#where-things-live).

### Release channels

A tag is a release, and the tag's suffix is the channel:

| Tag | Channel | Who takes it |
|---|---|---|
| `v1.2.3` | production | every station |
| `v1.2.3-beta.1` | beta | stations set to beta, alpha or dev |
| `v1.2.3-alpha.1` | alpha | alpha and dev |
| `v1.2.3-dev.4` | dev | dev only |

A channel is a risk tolerance, not a branch — there is one history of releases and a station's
channel says how early in it that station picks them up. Lower channels take everything above them
too, so a station on beta still gets production releases; it just sees the betas first. Put the
spare machine on beta and let it find the problems.

Set it per station in **Settings → Updates**, or as `updates.channel`. It defaults to production, so
a station only ever leaves it deliberately. Updates never go backwards, so returning from dev to
production means installing that build by hand once.

> **Channels need 0.15.0 or later.** A station on an older build ships an updater that asks GitHub
> only for the latest *finished* release, so it cannot see a prerelease whatever its config says.
> Those stations pick up the next production release automatically and can follow a channel from
> then on.

### Cutting a release

```bash
git tag v0.18.0 && git push origin v0.18.0
```

The workflow builds `win-x64`, `osx-arm64` and `osx-x64`, bundles the macOS builds into ad-hoc
signed `.app`s, marks anything with a channel suffix as a GitHub prerelease, and publishes every
asset with a `SHA256SUMS.txt`. The tag is the whole mechanism: it is stamped into the build's
`InformationalVersion`, and the app reads it back to know its own version and channel.

<details>
<summary><strong>How a station finds an update</strong>, since it is not the obvious way</summary>

A production station asks `/releases/latest` — one request, exact, assets included.

Prerelease channels cannot use that endpoint, which excludes prereleases by design. The `/releases`
collection endpoint would be the obvious answer and turned out to be unusable: it returned `200 []`
for this repository for long stretches while the releases were plainly there, and a station
believing it would have reported itself up to date.

So prereleases are discovered from the **tag list** and resolved through `/releases/tags/…`,
newest first, stepping over tags that have no release behind them. That last part is not defensive
programming for its own sake — a tag whose build failed is an ordinary event, and there are several
in this repository's history.

</details>

---

## Building, and contributing

```bash
dotnet run --project src/SundayReady
```

Avalonia · C# · .NET 9 · MVVM (CommunityToolkit.Mvvm). Published self-contained for `win-x64`,
`osx-arm64` and `osx-x64`. Requires the .NET 9 SDK to build; nothing to install to *run* a release.

**Contributions are welcome**, and the most valuable ones are not code:

- **Tell me it broke.** Open an issue. "It wouldn't start on my Mac" is a genuinely useful bug
  report — especially right now, because the macOS builds are newer and less exercised than the
  Windows ones.
- **Tell me what your booth does** that this cannot express. The verifier list is short on purpose,
  and the gaps are best found by people whose gear I have never seen.
- **Share a checklist.** If you have written a good one for a kind of station this does not ship a
  sample for, that is worth more than a feature.
- **Code**, if you like. Match the surrounding style: comments explain *why*, not *what*, and this
  codebase leans on them heavily to record the traps it has already fallen into.

Requirements: Windows 10 or 11, or macOS 12 and later.

## Licence

[PolyForm Noncommercial 1.0.0](LICENSE.md).

In plain terms, and this is a summary rather than the licence itself — **the file is what counts**:

- **Churches, charities, schools and personal use: yes, freely.** Use it, run it on as many
  machines as you like, change it, share your changes. That covers essentially everyone this was
  written for.
- **Selling it, or using it commercially: no.** Not without a separate licence.
- **Contributions are welcome.** By opening a pull request you keep your copyright, and you grant
  the project's maintainer the right to use your contribution under this licence *and* to include
  it in a future commercially-licensed version. That second half matters: without it a single
  contributor could freeze the project's licence forever, and every contributor would have to be
  tracked down and asked before anything could change. Nothing about your contribution stops being
  free for churches.

If you want to use it commercially, ask — the answer may well be yes, it just needs a conversation.

[releases]: https://github.com/bowlsbeyk/sundayready/releases/latest
