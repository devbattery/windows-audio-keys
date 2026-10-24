# Windows Audio Keys

This is a Windows 11 foreground-application audio hotkey utility.

## Roles

- Codex owns requirements, design, implementation instructions, build/test execution, and review.
- All application code, test code, build scripts, and implementation fixes must be written through OpenCode with model `opencode/deepseek-v4.1-flash-free`.
- When you are OpenCode: you are the implementer. Implement and test the assigned task directly; do not delegate back to Codex or spawn other agents.
- OpenCode CLI: `%LOCALAPPDATA%\Programs\@opencodedesktop\resources\opencode-cli.exe`.

## Scope and safety

- Source/build work stays inside this project directory. The finished app may keep its own settings under %LOCALAPPDATA%\GameAudioKeys; tests must use temporary settings directories. Do not touch other repositories, unrelated global configuration, credentials, or other applications' files.
- Never modify system/master endpoint volume. Operate only explicitly selected application audio sessions.
- Use Windows APIs out of process. No process injection, driver, memory patching, protection bypass, or low-level keyboard hook.
- Keep implementation small, dependency-free, and readable. Preserve failures as actionable Korean UI messages.
- If Windows blocks an executable or assembly under code-integrity/application-control policy, stop executing it and report the exact error. Do not retry through copying, renaming, reflection/in-memory loading, elevation, or security exclusions; do not change protection settings.
- The application is implemented. APP_TASK.md records its baseline contract; follow the current user request and current review instructions for further work. No application registration or allowlist. Earlier TASK.md and PROBE_TASK.md are historical proposals.
- Git remote: https://github.com/devbattery/windows-audio-keys.git, main branch. Keep local model logs, machine-specific diagnostic captures, user settings, and build outputs out of source commits.
- Public documentation must describe a general foreground-application audio utility. Do not include personal application usage, application-specific anecdotes, or private troubleshooting records. Keep executable/config paths accurate. Use neutral product-focused commit messages.
