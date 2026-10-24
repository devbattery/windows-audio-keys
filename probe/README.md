# AudioProbe (feasibility probe)

Small console probe that answers one question: **can we mute only the audio of the
currently foreground application, with no registration list?** It is not the full app.

- Native Core Audio COM interop (`IMMDeviceEnumerator` → `IAudioSessionManager2` →
  `IAudioSessionEnumerator` → `IAudioSessionControl2` → `ISimpleAudioVolume`).
- All **active render** endpoints are enumerated. Capture/microphone sessions and
  endpoint/master volume are never touched.
- C# 5, .NET Framework 4.x, in-box `csc.exe` + `System.Web.Extensions`
  (`JavaScriptSerializer` for JSON). No NuGet, no downloads.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\probe\build.ps1
```

Output: `bin\AudioProbe.exe` (source is UTF-8; the script passes `/codepage:65001`
and references `System.Web.Extensions.dll` from the same framework directory).

## Usage

```powershell
# Read-only: foreground window/process + every active render session (JSON)
.\bin\AudioProbe.exe --list

# Pure logic assertions; does not touch audio
.\bin\AudioProbe.exe --self-test

# Mutating probe. Refuses unless the real foreground PID == NUMBER.
.\bin\AudioProbe.exe --test-foreground --expect-pid 1234
```

Exit codes: `0` ok, `1` failure, `2` no matching session (explicit unsupported result),
`3` refused (foreground PID mismatch / focus changed / no foreground window),
`4` enumeration error, `64` usage error.

## Behaviour of `--test-foreground`

1. Reads `GetForegroundWindow` + `GetWindowThreadProcessId`; refuses on any PID
   mismatch before enumerating or mutating anything.
2. Snapshots processes (Toolhelp32: PID, parent PID) and resolves creation times with
   `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `GetProcessTimes`, plus image
   paths with `QueryFullProcessImageName`. Access failures are reported, not escalated.
3. Selects sessions whose PID equals the foreground PID, or a verified descendant whose
   **absolute executable path matches the foreground image** (`OrdinalIgnoreCase`).
   Chromium/Electron audio-service helpers share the browser image, so they match;
   a child running a different executable (terminal, launcher, updater) does not.
   Every child→parent edge must exist and have known creation times with
   parent ≤ child, so cycles, missing links, unknown times and recycled PIDs are
   rejected. Ancestors/launchers are never followed and names alone are never matched.
   System-sounds, cross-process (`AUDCLNT_S_NO_SINGLE_PROCESS`) and expired sessions are
   never targets. Zero targets means `no-session`; there is **no** master-volume fallback.
4. **Preflight (fail closed):** immediately before muting it re-reads every selected
   session's state, mute, volume and instance id. If any selected session is not fully
   readable, the entire run aborts with `status=preflight-failed` and nothing is muted.
   A bad session is never dropped to fake a partial success.
5. Re-checks the foreground PID immediately before muting.
6. Mutes only the targets, reads back, holds 2000 ms, then restores each target's exact
   original mute in `finally`, reads back again, and verifies **all** non-target sessions
   during the hold *and* after restore. Volume is only read, never set. Restoration is
   attempted for every session even if one fails.
7. Prints one JSON document with foreground↔session PID mapping, per-session
   before/during/after evidence, `muteSucceeded`, `restoreSucceeded`,
   `targetVolumeUnchanged`, `nonTargetSessionsUnchanged`, and an `errors` list.
   Any failure exits nonzero.

## Known limitations (honest)

- This probe controls application audio sessions, not individual browser tabs.
  Tabs/windows sharing a browser session can be muted together; tab-level targeting
  would require a different integration and is not provided by this probe.
- Descendant matching is deliberately narrow: only helpers that run the **same
  executable image** as the foreground process. A helper in another executable (a
  separate audio host, a child terminal, a launcher) is reported as
  `different-executable` / no session instead of being muted.
- Exclusive-mode audio and some brokered/remoted sessions may expose no usable
  `ISimpleAudioVolume`; they surface as no-session/unsupported, not as success.
- Sessions of elevated/protected/other-user processes may be invisible or unreadable.
- Focus can always change after the pre-mute re-check; the hold is short for that reason.
- `--list` intentionally omits window titles and browser URLs.

## Actual verification

Machine-specific diagnostic captures stay local and are not included in this repository.
Use the commands above to verify the foreground application on your own computer.
