# Settings

Reference for the app-settings system and how a setting flows from data to UI.
**Read this file when** editing `SettingsStore`, `SettingsPage`, `CodeFonts`, `SystemFonts`,
`FolderLauncher`'s editor command, or adding a new setting.

App settings live in a static `SettingsStore` — one source of truth for every window
and, via a watcher on `settings.json`, every process (reload on external write; open settings
pages re-read on the UI thread).

- Persisted to `settings.json` under the app's local-data directory (same place as
  `recent.json`), loaded once at startup. Registered in `AppJsonContext`.
- **Adding a setting** = add a `SettingSpec` to `SettingsStore.Specs` + a `GetValue`/`SetValue`
  case; the data-driven settings page renders the row automatically (kinds: text / choice /
  toggle).
- **UI**: a Settings button in the sidebar bottom status row opens a modal overlay on the main
  page. Changes apply live (no Save button).

## Settings in use

- **Default IDE/Editor** (`editor.command`, default `code`) — runs `<command> <folder>`;
  errors surface as a toast. (See also
  [`session-sidebar.md`](session-sidebar.md) for the sidebar's editor/folder buttons.)
- **Send message default** (`send.mode`) — see "Interrupt / send-while-busy" in
  [`session-state.md`](session-state.md).
- **Expand skills via slash commands** (`command.skills`, default on) — see "Slash-command send"
  in [`suggest-box.md`](suggest-box.md).
- **Auto-continue on thinking stop** (`turn.autocontinue`, default off) — see "Turn-stop handling"
  in [`session-state.md`](session-state.md).
- **Code font** (`text.codefont`, default per-platform) — the monospaced font used only where the
  content genuinely represents code or a terminal: markdown code blocks + inline code, diff/patch
  bodies, tool output that is file content/terminal text, and the shell tool's title line.
  Deliberately **not** applied to UI chrome (tool titles, chevrons, suggestion list, permission
  bodies, question text, error lines).
  The empty-string default maps to a font that ships with the OS — **Consolas** on Windows,
  **DejaVu Sans Mono** on Linux, **Menlo** on macOS — because a single hardcoded `Consolas`
  silently falls back to sans on Linux/macOS. The picker lists every installed font, enumerated
  lazily on first settings open.