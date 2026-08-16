# Dev environment

Reference for the Linux dev machine's runtime details.
**Read this file when** running, debugging, or logging the app on the Linux dev machine.

- A dev `opencode serve` is normally left running on `http://localhost:4196` (manual instance);
  use it as the positional URL argument for day-to-day runs (`UnoVibe http://localhost:4196`).
  (See also "How to Build & Run" in AGENTS.md for verification and startup.)
- Logging for the app run goes to `/mnt/LinuxProgramData/tmp/opencode/app_run.log`.
  Harmless X11 warnings about `_NET_WM_STATE` / `OverlappedPresenter` appear on launch and can be
  ignored.
- **Tips about `unovibe` CLI and environment variables:**
  the `unovibe` CLI command is not put on PATH automatically, so users can't use it without adding
  it manually. That's why those hints are commented out for now, until installing the CLI command
  is supported.