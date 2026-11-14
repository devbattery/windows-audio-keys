# Windows Audio Keys

This is a Windows 11 foreground-application audio hotkey utility.

## Roles

- The coding agent working in this repository (Claude) owns requirements, design, implementation, build/test execution, and review, and writes all application code, test code, build scripts, and fixes directly.
- Do not delegate implementation to other model CLIs. The earlier OpenCode delegation rule is retired.

## Scope and safety

- Source/build work stays inside this project directory. The finished app may keep its own settings under %LOCALAPPDATA%\GameAudioKeys; tests must use temporary settings directories. Do not touch other repositories, unrelated global configuration, credentials, or other applications' files.
- Never modify system/master endpoint volume. Operate only explicitly selected application audio sessions.
- Use Windows APIs out of process. No process injection, driver, memory patching, protection bypass, or low-level keyboard hook.
- Keep implementation small, dependency-free, and readable. Preserve failures as actionable Korean UI messages.
- If Windows blocks an executable or assembly under code-integrity/application-control policy, stop executing it and report the exact error. Do not retry through copying, renaming, reflection/in-memory loading, elevation, or security exclusions; do not change protection settings.
- The application is implemented. APP_TASK.md records its baseline contract; follow the current user request and current review instructions for further work. No application registration or allowlist. Earlier TASK.md and PROBE_TASK.md are historical proposals.
- Git remote: https://github.com/devbattery/windows-audio-keys.git, main branch. Keep local model logs, machine-specific diagnostic captures, user settings, and intermediate build outputs out of source commits. User requested a ready-to-run repository: the root WindowsAudioKeys.exe and its .config are intentionally versioned deliverables.
- Current user-requested work is described in STARTUP_TASK.md: a root executable and configurable elevated startup at current-user login. This supersedes the earlier no-startup scope in APP_TASK.md. User explicitly authorizes the product's own per-user scheduled task through its setting.
- The elevated startup task may only be registered when the executable lives under Program Files and every file/folder on that path is admin-only (src/InstallLocation.cs). Never relax this check: an elevated task pointing at user-writable files is a silent privilege escalation.
- Update check (src/UpdateCheck.cs) is notify-only: it reads GitHub release metadata and opens only the release page URL built from a validated version. Never add self-download, self-replace, or opening server-supplied URLs.
- Releases: the product version lives only in src/AssemblyInfo.cs. package.ps1 builds, tests and writes dist\WindowsAudioKeys-v<version>.zip plus .sha256 for GitHub Releases; dist/ is never committed. Tags, pushes and published releases happen only on explicit user request. The project is MIT licensed (LICENSE).
- Public documentation must describe a general foreground-application audio utility. Do not include personal application usage, application-specific anecdotes, or private troubleshooting records. Keep executable/config paths accurate. Use neutral product-focused commit messages.
