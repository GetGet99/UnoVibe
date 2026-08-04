# AGENTS.md

Guidance for AI coding agents working in this repository. This is general context about the project and environment — not task-specific instructions.

**Keep this file (project AGENTS.md) current.** When any fact here changes (e.g. project restructure, new/renamed files or folders that alter the source layout, a changed build/run command, a new service or dependency, updated package/SDK versions), update AGENTS.md to match — do not leave it outdated.

## What This Project Is

**UnoVibe** is a desktop chat client for [opencode](https://opencode.ai) built with Uno Platform. It talks to an `opencode serve` HTTP server over a minimal REST + SSE protocol and renders the chat session (messages, session list, tool views, questions) in a Skia-rendered desktop UI.

High-level goals/design:
- App should be **self-contained**: it can launch its own local `opencode serve` from a user-picked folder, or connect to an existing server.
- Uses **QuickMarkup** (declarative reactive UI DSL, Vue-inspired) instead of XAML.
- Targets the **desktop** Skia renderer only (`net10.0-desktop`). Android/iOS/WebAssembly targets are commented out in the csproj.

## Tech Stack

- **Uno Platform** via `Uno.Sdk` (see `global.json`, currently `6.6.29`). Do **not** bump individual Uno package versions — update the SDK version in `global.json` instead.
- **.NET 10** (`dotnet --version` → `10.0.110`, `net10.0-desktop`).
- **QuickMarkup** `0.1.20` (`QuickMarkup.Uno` package; versions pinned in `Directory.Packages.props`). Uses central package management.
- Only external package reference: `QuickMarkup.Uno`. Everything else comes from the Uno.Sdk implicit packages.
- Build uses `Uno.SingleProject`; `EmitCompilerGeneratedFiles=true` so generated source lands under `UnoVibe/obj/<tfm>/generated/...`.

## How to Build & Run

At the start of a new session, verify the dev server is actually running before assuming it is — the machine may have been restarted. Check with:

```bash
ps aux | grep "opencode serve" | grep -v grep
```

If `http://localhost:4196` is not up, start it manually (or use the ConnectPage "Local server" flow):

```bash
nohup opencode serve --port 4196 > /mnt/LinuxProgramData/tmp/opencode/serve_dev.log 2>&1 & disown
```

```bash
# Build (desktop target)
dotnet build UnoVibe/UnoVibe.csproj -f net10.0-desktop

# Run (this is the dev-workflow launch; app forks and stays in background)
cd /mnt/Data/Codes/UnoVibe
OPENCODE_BASE_URL=http://localhost:4196 nohup dotnet run --project UnoVibe/UnoVibe.csproj -f net10.0-desktop --no-build \
  > /mnt/LinuxProgramData/tmp/opencode/app_run.log 2>&1 & disown

# Relaunch (kill only the app; leave servers alone)
pkill -9 -x UnoVibe
```

Without `OPENCODE_BASE_URL` set, the app shows `ConnectPage` (connect to existing server, or pick a folder and run `opencode serve` there). With it set, it configures `ChatStore` and shows `MainPage` directly.

Note: `pkill -f "opencode serve ..."` and similar broad patterns can hang the shell session in this environment — prefer `pkill -9 -x <exact-name>` or `pkill -f` with a unique exact port, and check with `ps aux | grep "opencode serve"` afterward.

**CRITICAL — do NOT kill the dev `opencode serve` (port 4196) during this chat session**: this opencode session itself is served by that instance, so killing it terminates the chat and the command. If a server-side change (e.g. `small_model`/auth/setting edits in `~/.config/opencode/opencode.jsonc`, global opencode.json, or plugins) requires a restart to take effect, ask the user to restart it themselves rather than running `pkill -9 -x opencode` (or dropping into the session's own TUI to `/session` restart).

## Source Layout

- `UnoVibe/Pages/` — top-level pages: `ConnectPage`, `MainPage`, `ChatPage`.
- `UnoVibe/Controls/` — reusable UI: `SessionSidebar`, `MessageView`, `ToolViews/*` (ToolView* render opencode tool calls).
- `UnoVibe/Services/` — core logic:
  - `OpencodeClient.cs` — minimal HTTP client for the opencode REST API; Basic-auth capable.
  - `ChatStore.cs` — reactive singleton store for the active session; owns SSE event pump, messages, sessions, settings; owns any locally-launched `ServeProcess`.
  - `EventStreamReader.cs` — reads the SSE `/event` stream.
  - `ServeProcess.cs` — launches `opencode serve --port <free>` in a folder, waits for health, generates a strong password.
- `UnoVibe/Models/` — DTOs (`MessageItem`, `SessionInfo`, `ModelOption`, `ToolView*` item types, etc.).
- `App.xaml.cs` — startup routing + `NavigateToMain()` (swaps `MainWindow.Content`).

## QuickMarkup

**Always load the skill** when editing QuickMarkup UI: `.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo). Key gotchas learned the hard way:

- A `[QuickMarkupConstructor]` method **must call `Init()`** (usually first) or the UI tree never builds.
- Only `Reference<T>` fields declared in the `[QuickMarkup("""...""")]` header are reactive. Plain `ObservableCollection.Count` in an `if` condition is NOT reactive; with `&&` short-circuiting, at least one Reference must be read first to subscribe.
- Two-way binding is `` Property<=>`Var` ``. `CheckBox.IsChecked` is `bool?` and two-way binding it to a `bool` field will not compile — use `ToggleSwitch` (`IsOn` is `bool`) instead.
- Values in markup are not quoted; use backticks for C# expressions, `<>...</>` for collection-typed properties, `if (`expr`) { }` for conditional children.
- The QuickMarkup skill lives in `.agents/skills/quickmarkup/SKILL.md` (committed). The upstream source is at `/mnt/Data/Codes/QuickMarkup/wt-master/`.

## Referenced / Cloned Projects

- **QuickMarkup source**: `/mnt/Data/Codes/QuickMarkup/wt-master/` — read this to understand markup syntax, the source generator, and what binds compile. Its own skill: `/mnt/Data/Codes/QuickMarkup/wt-master/.agents/skills/quickmarkup/SKILL.md` and `docs/qm-language.md`.
- **Uno Platform source**: `/mnt/Data/Codes/.GitHubClone/uno/` — useful for platform API behavior (e.g., X11 `FolderPicker` via desktop portal at `X11ApplicationHost.cs`; `FolderPicker.skia.cs` throws `NotSupportedException` if the extension is missing).
- **opencode source**: `/mnt/LinuxProgramData/tmp/opencode/opencode-src/` — server API/auth reference. Auth lives in `packages/opencode/src/server/auth.ts`.

## opencode Server Integration

- **Auth**: Basic auth `Authorization: Basic base64(username:password)`. Env vars: `OPENCODE_SERVER_PASSWORD`, `OPENCODE_SERVER_USERNAME` (default username `opencode`). Password empty/unset ⇒ unsecured. **Every** endpoint requires auth when a password is set — including `GET /global/health` — so health/startup probes must send the header too.
- **Startup readiness**: poll `GET /global/health` until it returns `{"healthy":true,...}`.
- **SSE events**: `GET /event` (long-lived stream).
- **Session API**: `POST /session` (create; omit `title` so the server assigns a default and auto-generates a name — see "Titles"), `GET /session` (list), `PATCH /session/:id` with `{ title }` (rename; this is how the TUI renames and how the server's title generator writes names), `POST /session/:id/abort` (interrupt the running turn).
- **Titles**: `POST /session` with no title yields a default `"New session - <ISO>"`/`"Child session - <ISO>"`. On the first prompt the server runs a `title` agent with the small model (`provider.getSmallModel`) and replaces the default via `session.setTitle` (source: `session/prompt.ts` `SessionPrompt.ensureTitle`; regex in `session/session.ts` `isDefaultTitle`). The write emits a `session.updated` event carrying `{ sessionID, info }`, which `ChatStore.ApplySessionUpsert` applies to the sidebar + header. UnoVibe creates sessions without a title, displays `"New Chat"` for default-titled sessions (`NormalizeTitle`), and surfaces the generated name when the event arrives. Manual rename (`ChatStore.RenameSessionAsync`, header ✎ button) calls `PATCH /session/:id` and short-circuits future auto-naming because the title no longer matches `isDefaultTitle`.
- **Permission API**: `GET /permission` (list pending), `POST /permission/:requestID/reply` with `{ reply: "once"|"always"|"reject", message? }`. Events `permission.asked` (properties = the full `PermissionV1.Request`: `{ id, sessionID, permission, patterns[], metadata{}, always[], tool?: {messageID, callID} }`) and `permission.replied` (`{ sessionID, requestID, reply }`). `ChatStore` keeps a pending-request queue (`ActivePermission` = oldest pending) and does NOT session-filter these events (subagents run in their own sessions). The UI shows an inline allow/always/reject dialog above the input and disables sending while one is pending.
- **Status / errors**: `session.status` events carry `{ sessionID, status: {type:"idle"|"busy"|"retry", attempt?, message?, action?, next?} }`; the TUI treats anything `!= "idle"` as busy and shows the retry message. `ChatStore.StatusMessage` surfaces the retry banner. Assistant message errors (`info.error`) are rendered as an `error` part box (`UnknownError` e.g. `"Streaming response failed: [503]..."`); `MessageAbortedError` maps to the interrupted part instead. Error message strings may contain surrounding literal quotes — `UnwrapErrorMessage` strips them.
- **Unhandled events**: `ChatStore.Apply` has `// TODO:` placeholder `case`s (with `break;`) for every other event the server's `/event` stream emits (`message.removed`, `session.deleted/error/diff/idle/compacted`, `question.replied/rejected`, `file.edited`, `file.watcher.updated`, `vcs.branch.updated`, `todo.updated`, `lsp.updated`, `command.executed`, `mcp.tools.changed`, `mcp.browser.open.failed`, `server.connected/heartbeat/instance.disposed`, `tui.toast.show`). `session.created` and `session.updated` are already handled (`ApplySessionUpsert`). The `session.next.*` streaming events exist in the schema but are not published by the current CLI server. Implement a case and remove its TODO marker when adopting it.
- `opencode serve` flags: `--port` default 0 (random), `--hostname` default `127.0.0.1`. Server instance is resolved per-request via the `x-opencode-directory` header, so it can be launched from any directory.
- Port probing at runtime should use a real bind (e.g., `TcpListener` on `127.0.0.1:0`, or Python `socket`); bash `shuf` can pick an occupied port.
- **Interrupt / send-while-busy**: `ChatStore.InterruptAsync()` calls `POST /session/:id/abort` (the server cancels the runner + in-flight tools and marks aborted tool parts with `state.metadata.interrupted=true` and the assistant message `error.name === "MessageAbortedError"`).
- **Send while busy**: fire `prompt_async` immediately even when a turn is running — the server serializes it itself. `createUserMessage` stores the prompt at once; the running session loop picks it up at the **next agent step** (after the in-flight tool call), not at full idle. This matches the TUI (`stream.transport.ts` `runPromptTurn` calls `promptAsync` regardless of busy; its `state.wait` gate only prevents a second concurrent UI submit). `ChatStore.SendAsync` always sends immediately and defers ordering to the server. A client-side queue (`PendingPrompts`/`EnqueuePrompt`/`DrainPendingPromptsAsync`) is kept dormant behind a `TODO(settings/queuing)` for a future "queue on client" mode.

## Known Working State / Conventions

- A dev `opencode serve` is normally left running on `http://localhost:4196` (manual instance); use it as `OPENCODE_BASE_URL` for day-to-day runs.
- The ConnectPage "Local server" flow generates a cryptographically-random 32-char password by default (so only this app can connect), or accepts a custom password + confirmation. The spawned server is owned by `ChatStore.AttachServeProcess(...)` so it survives navigation — do not re-introduce a `using var serve` that disposes it early.
- Logging for the app run goes to `/mnt/LinuxProgramData/tmp/opencode/app_run.log`. Harmless X11 warnings about `_NET_WM_STATE` / `OverlappedPresenter` appear on launch and can be ignored.
