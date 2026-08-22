# Settings

Reference for the app-settings system and how a setting flows from data to UI.
**Read this file when** editing `SettingsStore`, `SettingsPage`, `CodeFonts`, `SystemFonts`,
`FolderLauncher`'s editor command, or adding a new setting.

App settings live in a static `Services/SettingsStore.cs` — one source of truth for every window
(static = shared in-process) and, via a `FileSystemWatcher` on `settings.json`, every process
(reload on external write, debounced + loop-guarded by the last-written content; `Changed` notifies
open settings pages to re-read on the UI thread).

- Persisted to `settings.json` under the app's local-data directory (same place as `recent.json`),
  loaded once at startup (`ConnectPage` ctor, like `RecentConnectionsStore`). Registered in
  `AppJsonContext` (`SettingsFileModel`).
- **Adding a setting** = add a `SettingSpec` to `SettingsStore.Specs` + a `GetValue`/`SetValue` case;
  the data-driven settings page (`Pages/Main/SettingsPage.cs`) renders the row automatically
  (kinds: text / choice / toggle, `Models/SettingsEntry.cs` reactive row model).
- **UI**: a ⚙ **Settings** button in the sidebar bottom status row opens a modal overlay on
  `MainPage` (`SettingsOpen` + `<SettingsPage OnClose=...>`); Back/Done close it. Changes are applied
  live (no Save button) via `SettingsStore.SetValue`.

## Settings in use

- **Default IDE/Editor** (`editor.command`, default `code`) — `FolderLauncher.OpenInEditor` runs
  `<command> <folder>`; errors surface as a toast. (See also
  [`session-sidebar.md`](session-sidebar.md) for the sidebar's editor/folder buttons.)
- **Send message default** (`send.mode`) — see "Send while busy" in
  [`session-state.md`](session-state.md).
- **Expand skills via slash commands** (`command.skills`, default on) — see "Slash-command send"
  in [`suggest-box.md`](suggest-box.md): off means skill-only `/name` text goes out as a plain
  prompt; real commands/MCP prompts always expand, and a name matching both a command and a skill
  runs the command.
- **Auto-continue on thinking stop** (`turn.autocontinue`, default off) — see "Turn-stop handling"
  in [`session-state.md`](session-state.md): when enabled, a turn that stops with the chat ending
  on an unfinished Thinking (reasoning) part gets a `continue` prompt sent automatically instead of
  surfacing the Continue button, silently (no completion toast, no sidebar unread/check mark).
- **Code font** (`text.codefont`, default per-platform) — the monospaced font used only where the
  content genuinely represents code or a terminal: markdown code blocks + inline `` `code` ``,
  diff/patch bodies, tool output that is file content/terminal text (read loaded content, write/edit
  output, generic tool input/output), and the shell tool's `$ command` title line.
  It is deliberately **not** applied to UI chrome: tool title/header labels (except the shell command
  line), expand/collapse chevrons + "Show more/less" buttons, the `/`-command suggestion list,
  permission bodies, question text, or `ToolError` lines.
  `Services/CodeFonts.cs` resolves the setting into a `FontFamily` everyone binds to
  (`CodeFonts.Current`); the empty-string default maps to a font that ships with the OS —
  **Consolas** on Windows, **DejaVu Sans Mono** on Linux (the fontconfig `monospace` default on
  nearly every distro), **Menlo** on macOS — because a single hardcoded `Consolas` silently fell
  back to the default sans font (no monospace) on Linux/macOS where the Microsoft font doesn't
  exist. Any other installed monospaced font name works verbatim, or the `monospace` generic on
  Linux. The picker lists every font installed on the device, enumerated once (lazily, on first
  settings open) by `Services/SystemFonts.cs`: SkiaSharp's `SKFontManager.Default.FontFamilies`
  on desktop (the same font manager that resolves `FontFamily` in the Skia renderer), and
  Win2D's `CanvasTextFormat.GetSystemFontFamilies()` (DirectWrite) on the `net10.0-windows` target.