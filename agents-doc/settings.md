# Settings

Reference for the app-settings system and how a setting flows from data to UI.
**Read this file when** editing settings or adding a new setting.

App settings live in a static `SettingsStore` — one source of truth for every window
and, via a watcher on `settings.json`, every process. Changes apply live (no Save button).

- **Adding a setting** = add a `SettingSpec` to `SettingsStore.Specs` + a `GetValue`/`SetValue`
  case; the data-driven settings page renders the row automatically.

## Product decisions

- **Send message default** (`send.mode`) — decides what a send does while a turn is running.
  See "Interrupt / send-while-busy" in [`session-state.md`](session-state.md).
- **Auto-continue on thinking stop** (`turn.autocontinue`, default off) — see "Turn-stop handling"
  in [`session-state.md`](session-state.md).
- **Expand skills via slash commands** — see "Slash-command send" in
  [`suggest-box.md`](suggest-box.md).
- **Code font** — used only where the content genuinely represents code or a terminal,
  deliberately **not** applied to UI chrome (tool titles, suggestion list, permission bodies,
  question text, error lines). The empty-string default maps to a font that ships with the OS
  (Consolas / DejaVu Sans Mono / Menlo) because a single hardcoded `Consolas` silently falls
  back to sans on Linux/macOS.
- Image attachments are deliberately **not** gated on model `capabilities.attachment` at attach
  time (mirrors TUI/web); guarding image-incapable models is a known follow-up.
