# Current implementation task: simple launch and elevated login startup

Implement via OpenCode DeepSeek only. Work in this repository. User authorizes the product to configure its own elevated login task when the new setting is explicitly enabled. No unrelated scheduler/system/security changes. Do not commit/push: Codex reviews and publishes. Do not operate the UI or configure a real scheduled task yourself.

## Deliverable and scope

- Canonical ready-to-run product is `WindowsAudioKeys.exe` and `WindowsAudioKeys.exe.config` at repository ROOT. User can download the GitHub repository ZIP and run this immediately; Codex will commit these two release files. Build/test helpers stay ignored under bin. Update build.ps1 to compile/test then publish the matching product pair at root. A running root EXE must produce a clear close-before-build error, never kill user processes.
- Show product name `Windows Audio Keys` in the UI, tray and ordinary messages. Preserve the existing internal namespace, user settings directory, JSON compatibility, existing hotkeys and single-instance mutex; do not lose existing settings or accidentally create a parallel legacy instance. Do not touch unrelated audio logic or add keyboard diagnostics.
- README: first instruction is double-click root WindowsAudioKeys.exe; general foreground-application audio utility only, no personal app names/use anecdotes. Document settings, elevated startup and location stability concisely. No new installer/updater/services/dependencies.

## Elevated automatic start setting

Use the Windows Task Scheduler COM API (Schedule.Service), not a Run registry entry/Startup shortcut/SYSTEM service/password storage. Native Windows platform only (.NET Framework 4.8 C#5). Microsoft.CSharp dynamic COM is fine; no NuGet.

- Add a clearly labelled setting row/group: `Windows 로그인 시 관리자 권한으로 자동 시작`, checkbox, `자동 시작 적용` button, status. Separate application of this setting from the audio settings Save button so disk saves and scheduler registration don't become a distributed rollback transaction. Read ACTUAL task state as source of truth, not a boolean that claims success before registration. Default off if no owned task. Query failures visible and must not appear as successfully disabled.
- One stable task owned by this app and scoped to current user's SID, e.g. `WindowsAudioKeys-<SID>` in root task folder. Principal current user SID, InteractiveToken (3), RunLevel Highest (1), LogonTrigger (9) ONLY this SID, ExecAction (0) pointing to the full absolute path of THIS product executable, working directory its parent, argument `--startup`. No passwords, no SYSTEM, no trigger for all users, no machine boot trigger. Enable battery operation; no finite execution time limit; ignore new concurrent instances.
- Include an ownership marker in description and validate marker + principal SID before replacing/deleting a colliding task. A differently owned task must be preserved and show an error. Reading matching status checks correct enabled principal/run level/trigger/action/path/args. If an owned task refers to an old location, show that it needs registration refresh. Disabling removes only this exact owned per-user task and is idempotent when absent. Do not remove the task folder or other tasks.
- Manual executable launch stays asInvoker (not forced admin every time). When configuring startup from a non-elevated process, use standard `ProcessStartInfo` RunAs/UAC to run the SAME exe in a narrowly parsed helper mode such as `--configure-startup enable|disable --request-user-sid <SID>`. Handle helper mode BEFORE single-instance mutex. Helper must verify current Windows identity SID equals requested SID (reject alternate-admin credentials changing the user), validate exact command syntax, apply only this app's task with its OWN executable path, then exit. No arbitrary executable/task-name/XML/script/command arguments. If already elevated, use the same service directly. Never disable UAC or change security policy.
- Parent UI must remain responsive while the consent/helper runs. Cancelled UAC or failure keeps actual task state unchanged/requeries it and shows a clear Korean message. Refresh real state on success. Restore usable buttons even on error/disposal. Error details should be actionable; no blind retries.
- Show current elevation state and a short note that enabling/disabling this option may need one Windows administrator approval. Enabling doesn't silently change current app permissions; automatic starts will be elevated. Keep audio settings independent.

## Startup launch behavior and compatibility

- `--startup` starts quietly in tray with loaded settings, no startup dialog. Existing instance -> silent success in startup mode, ordinary duplicate launch retains clear user guidance. Corrupt config can still bring up settings/error.
- Manual launch opens settings. Beware: assigning MainForm on ApplicationContext makes Application.Run show it automatically; current implementation did this. Ensure startup truly stays hidden without breaking message pump or tray lifetime/cleanup.
- UIA accessible names, layout without clipping, resize/DPI reasonable. Keep close-to-tray and visible Exit.
- Test-only settings overrides remain supported for app tests. Do not allow configuring real startup tasks while a test settings override is active; report it in UI/helper so QA can't silently register a temporary app location/config.

## Verification

- Build all and run existing 26 probe tests + 62 app tests. Add focused pure tests for startup CLI parsing/combination rejection, task XML/definition exact principal and one logon trigger/highest/interactive/path quoting and escaping, owned vs foreign task detection, missing vs unreadable status. Test code must not register tasks/elevate itself.
- Use existing shared patterns where useful. Keep task implementation small; actual elevated registration/UI QA belongs to Codex with the user later. DO NOT launch product, invoke RunAs, configure tasks, touch user settings, or log arbitrary keyboard input.
- If Windows blocks any new executable under application control/code-integrity, stop immediately and report exactly. No retry via alternate filename/location/reflection/elevation or protection exclusions.
- Build and report tested results and changed files. Finish promptly; read only directly relevant files (Program/MainForm/TrayAppContext/build/tests), not full old model logs or the large CoreAudio/probe source.

Reference: https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks and https://learn.microsoft.com/en-us/windows/win32/taskschd/starting-an-executable-when-a-user-logs-on
