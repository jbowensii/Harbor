# Release notes

## 0.2.0 — 3 September 2026

Installer release. No changes to the application itself.

**Windows installer**
`HarborSetup.exe` (Inno Setup) replaces the manual copy-into-place procedure. It is a per-user
install — no admin rights, no UAC prompt. The executable and icon go to `%LOCALAPPDATA%\harbor`,
a Harbor shortcut is placed on the desktop (icon taken from `harbor.ico`, not the exe), and an
uninstaller is registered.

**Configuration seeding**
First install writes an empty `servers.json` to `%APPDATA%\Harbor` — where Harbor actually reads
it. The file is marked *only-if-doesn't-exist*: reinstalling or upgrading never touches an
existing configuration, and uninstalling never deletes it.

**Signed binaries**
`Harbor.exe` and `HarborSetup.exe` are code-signed (SSL.com), so SmartScreen no longer warns on
first run.

The installer script lives in `build/installer/harbor.iss`.

## 0.1.0 — 2 September 2026

First release. Harbor stores your local dev servers, launches them, shows whether they are
actually up, and stops them cleanly.

### Features

**Server configuration**
Each entry holds a name, category, the exact command line, working directory, host, port, URL
path, per-entry environment variables and free-text notes. Commands run through
`cmd.exe /d /s /c`, so `npm run dev`, `python -m uvicorn …`, `dotnet run` and `.bat` files behave
as they do in a terminal — `.cmd` shims and PATH lookups included.

**Live status**
The TCP listener table is read every two seconds. Entries show as *Running* (Harbor owns the
process and the port is bound), *Starting* (process up, port not yet bound), *External*
(something is on the port that Harbor did not launch), *Crashed*, or *Stopped*. Non-loopback
hosts are checked with a short connect probe instead.

**Reliable stop**
`npm run dev` expands to cmd → npm.cmd → node → node. Killing the spawned process alone orphans
`node.exe` on the port. Harbor assigns the process to a Windows job object with
`KILL_ON_JOB_CLOSE`, so the whole tree terminates together — and still terminates if Harbor is
killed. Verified end to end: start binds the port, stop frees it and leaves no survivors.

**Monitor-only entries**
Mark an entry *Remote* and Harbor watches the port without ever starting it — for containers,
in-editor servers, or services on another machine.

**Categories**
Add, rename, delete and reorder the sections in the list. Order in the manager is order on
screen. An empty category still renders, and deleting one moves its servers to *Uncategorised*
rather than deleting them.

**Port conflict detection**
Any port claimed by more than one entry is named in a banner at the top of the window.

**Output pane**
Per-server stdout and stderr, stderr in red, capped at 800 lines and pinned to the newest line
while a server boots.

**Light and dark themes**
*System* follows the Windows app setting and reacts live; *Light* and *Dark* pin it. The choice
is stored and defaults to *System*. Switching repaints immediately, native title bar included,
with no restart.

**Logging**
`%APPDATA%\Harbor\harbor.log`, rolling at 1 MB. Records config loads and saves, server starts and
stops with pid and port, servers that exit on their own, and full stack traces on error. Config
loads also log a listing of the config folder as that process saw it.

### Design notes

- Configuration is written to a temp file and swapped in, so an interrupted save cannot truncate
  it. Where the atomic replace is refused — a sync client holding the file, or a redirection
  layer — Harbor falls back to a plain copy rather than losing the save.
- A config that fails to parse is preserved as `servers.json.broken-<timestamp>` instead of being
  overwritten.
- A config with neither servers nor categories is treated as damage and restored from
  `servers.seed.json`, if you have chosen to place one next to the exe. That state cannot arise
  in normal use — deleting the last server leaves the categories behind — so nothing
  deliberately removed is resurrected.
- The application icon is a crop of the source artwork, not a copy of it: the original has a thin
  ring and hairline detail that collapse below 48px, so the icon zooms past the ring and adds a
  hairline rim that survives 16px.

### Known limitations

- Windows only. The status detection reads the Windows TCP table and the process management uses
  Win32 job objects.
- Status detection is TCP-only. A UDP-based service will always read as stopped.
- Harbor does not restart a server that exits on its own; it reports the exit code and waits.
- There is no autostart-on-login option yet.

### Install

There is no installer. Copy `Harbor.exe` into a folder of your choice — `%APPDATA%\Harbor\` is a
good default — put `harbor.ico` beside it, and make a Desktop shortcut whose icon points at the
`.ico` rather than the exe. Full steps are in the README. Nothing is written to the registry and
no startup entry is created; uninstalling is deleting the exe and the `%APPDATA%\Harbor` folder.

No configuration ships with the release. The first launch starts with an empty list — press
**Add server**.

### Requirements

Windows 10/11 and the .NET 10 Desktop Runtime.
