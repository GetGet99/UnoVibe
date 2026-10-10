# AGENTS.md

Guidance for AI coding agents working in this repository.
This is general context about the project and environment — not task-specific instructions.

## Contribution guideline (AGENTS.md + agents-doc/)

### Documentation principle (code is the source of truth)

Same rationale as the `no-comments` rule in the user-level `AGENTS.md`:
docs rot like comments and then actively mislead.
The codebase itself must be the source of truth for what and how.

- Document only important product decisions: the reason a behavior exists,
  a constraint that is not obvious from reading the code,
  a banned pattern with its rationale, or an external contract the code must match.
- Do NOT document what readable code already says: file layouts, class/method names,
  UI widget details, default values, settings keys, or step-by-step implementation flows.
  Point to the code instead of copying it.
- When a fact changes, prefer deleting the stale narration over updating the mirror.
- Describe only what exists today (see "Removed features are removed everywhere" below).
  Transient status, roadmaps, and revision history do not belong here.

**What goes where:**

- **AGENTS.md holds rules** agents must follow, plus the most important facts needed for (almost)
  every task: project identity, stack constraints, build/run, and safe-operation rules.
- **`agents-doc/*.md` holds need-to-know detail**, read on demand when a task touches that area.
  See "Reference documentation" below for what each file covers and when to read it.
- Keep the split consistent when something changes: rules stay in AGENTS.md, detail lives in
  agents-doc/. Update both together when a rule's underlying detail changes.

**Removed features are removed everywhere.**
When a feature is removed, delete every mention of it from code, comments, docs, and scripts —
including AGENTS.md and agents-doc/. Do **not** document that the old feature existed
(no "the old X was replaced by Y", no "X is no longer supported"). Describe only what exists today.

**Keep lines under 150 characters (AGENTS.md and agents-doc/*.md).**
Break long lines at natural sentence/clause boundaries. Use sub-bullets for dense sections
instead of single massive paragraphs. This keeps diffs clean and the file scannable.
This does not apply to source code — follow the project's existing code style.
Before finishing any change that touches one of those files, run
`scripts/validate-markdown-lines.ps1` (Windows) or `scripts/validate-markdown-lines.sh`
(Linux/macOS) — it exits non-zero when any line exceeds the limit.

## Reference documentation (agents-doc/)

Everything an agent needs on a need-to-know basis lives in `agents-doc/`. Each entry states
**why the file is relevant** and **when to read it**. Read the file for your area before editing
that code, and keep it up to date alongside AGENTS.md (see "Contribution guideline").

- **`agents-doc/opencode-server.md`** — the `opencode serve` external contract.
  _Read before_ working on server API, SSE events, permissions/questions, MCP, or serve processes.
- **`agents-doc/integration.md`** — `UnoVibe.Integration` rules.
  _Read before_ adding or modifying API endpoints, DTOs, or `AppJsonContext` registrations.
- **`agents-doc/session-state.md`** — client-side per-session behaviors and why they exist.
  _Read before_ editing chat send/revert/fork/autoscroll behavior.
- **`agents-doc/session-sidebar.md`** — the sidebar model.
  _Read before_ editing `SessionSidebar` or session/busy/outcome indicators.
- **`agents-doc/quickmarkup.md`** — where the QuickMarkup rules live.
  _Read before_ writing or editing QuickMarkup markup.
- **`agents-doc/settings.md`** — the settings system and its product decisions.
  _Read before_ editing settings or adding a setting.
- **`agents-doc/polyfills.md`** — the per-OS `FolderPicker` polyfills and why they exist.
  _Read before_ touching folder pickers or `WindowsHelper`.
- **`agents-doc/notifications.md`** — desktop notifications and the in-app vs OS split.
  _Read before_ editing notification wiring or polyfills.
- **`agents-doc/connect-page.md`** — the ConnectPage flow and folder-security decisions.
  _Read before_ editing the connect/serve flows.
- **`agents-doc/responsive-layout.md`** — the compact-window layout system.
  _Read before_ changing page grid layouts or compact breakpoints.
- **`agents-doc/markdown-rendering.md`** — `MarkdownView` and its deliberate simplifications.
  _Read before_ editing message-text rendering.
- **`agents-doc/tool-views.md`** — tool-call rendering and the null-means-absent rule.
  _Read before_ editing tool views or apply_patch parsing.
- **`agents-doc/suggest-box.md`** — `SuggestBox` and server command/skill routing.
  _Read before_ editing suggestions or slash-command send.
- **`agents-doc/referenced-projects.md`** — Linux-only upstream source checkouts.
  _Read when_ you need upstream source answers.
- **`agents-doc/dev-environment.md`** — Linux dev-machine runtime notes.
  _Read when_ running/debugging/logging the app on the Linux dev machine.
- **`agents-doc/test-page.md`** — the dev-only tool-view harness.
  _Read before_ editing `UnoVibe/Pages/Test/*`.

## What This Project Is

**UnoVibe** is a desktop chat client for [opencode](https://opencode.ai) built with Uno Platform.
It talks to an `opencode serve` HTTP server over a minimal REST + SSE protocol and renders the
chat session (messages, session list, tool views, questions) in a Skia-rendered desktop UI.

High-level goals/design:
- App should be **self-contained**: it can launch its own local `opencode serve` from a
  user-picked folder, or connect to an existing server.
- Uses **QuickMarkup** (declarative reactive UI DSL, Vue-inspired) instead of XAML.
- Desktop-only: the **Skia** target (`net10.0-desktop`) everywhere, and on Windows additionally a
  **WinUI** target (`net10.0-windows10.0.26100.0`). Android/iOS/WebAssembly targets are commented
  out in the csproj.

## Tech Stack

- **Uno Platform** via `Uno.Sdk` (see `global.json`).
  Do **not** bump individual Uno package versions — update the SDK version in `global.json` instead.
- **.NET 10**. Targets: `net10.0-desktop` (Skia) and, on
  Windows only, `net10.0-windows10.0.26100.0` (WinUI) — the csproj gates the second TFM behind
  `$(OS) == 'Windows_NT'`.
- **QuickMarkup** (versions pinned in `Directory.Packages.props`, currently a
  locally-packed build of the upstream `wt-master` repo): `QuickMarkup.Uno` for non-Windows
  targets, **`QuickMarkup.WinUI`** + **`Microsoft.WindowsAppSDK`** for `net10.0-windows`.
  Uses central package management.
- Only external package references: `QuickMarkup.Uno`, `Markdig`, and `ColorCode.Core`
  (plus `QuickMarkup.WinUI`, `Microsoft.WindowsAppSDK`, and `Microsoft.Graphics.Win2D` on the
  Windows target). Everything else comes from the Uno.Sdk implicit packages.

### Native AOT constraints

`<PublishAot>true</PublishAot>` is set in the csproj, so reflection-based JSON is unavailable.
All JSON (de)serialization must go through source-generated `JsonSerializerContext` classes:
- **`Helpers/AppJsonContext.cs`** — app-layer types.
- **`UnoVibe.Integration/AppJsonContext.cs`** — API-layer types.

- Do NOT add new reflection-based `JsonSerializer.Deserialize<T>(..., JsonSerializerOptions)`
  calls, anonymous/Dictionary request bodies, or `JsonSerializerOptions` fields.
- Every request body and persisted model is a named class registered in the appropriate
  `AppJsonContext`.

**Uno platform quirks:**

- `Windows.Storage.Streams.DataReader.LoadAsync` is **not implemented in Uno** (Uno0001) —
  read `IRandomAccessStream` via `AsStreamForRead()` instead.
- `ComboBox.DisplayMemberPath`/`SelectedValuePath` are **banned** —
  Uno resolves the item property for those via a reflection-driven `BindingPath` that NativeAOT
  trimming breaks (the model combo rendered an empty label and dead selection under AOT only).
  Use `ItemTemplate` + an object-based `SelectedItem` binding instead.

### Compile-time OS constants

The csproj defines these constants for `#if`-gated OS-specific code (see the csproj
for the exact per-TFM conditions):

- **`DESKTOP_WINDOWS` / `DESKTOP_LINUX` / `DESKTOP_MACOS`** — the OS of a `net10.0-desktop`
  (Skia) build only; **never** defined on the `net10.0-windows10.0.26100.0` TFM.
- **`WINDOWS`** — any Windows-targeted build: a `net10.0-desktop` build with a `win-*` RID or on
  a Windows host, plus the `net10.0-windows10.0.26100.0` TFM (where the .NET SDK also
  auto-defines it).
- **`WASDK`** — the `net10.0-windows10.0.26100.0` (WinAppSDK) target only.

This replaces runtime `OperatingSystem.IsWindows()/IsMacOS()/IsLinux()` dispatch. The
`net10.0-desktop` build's OS still comes from `DESKTOP_*` where Skia/WinUI behavior differs; pure
OS behaviors that are identical on WinAppSDK use the broader `WINDOWS` guard instead.

## Windows Build (WinUI) Conventions

The Windows **WinUI** target is supported and should be kept compilable, so follow these
conventions when writing cross-target code. On a Linux dev environment you **cannot** build
`net10.0-windows` — there's no way to compile/verify the Windows target here — so write code
that follows the portable forms below to avoid breaking Windows later. (On Windows the dev
machine also lacks the reference clones in `agents-doc/referenced-projects.md` — Linux-only paths.)

- **Windows has no `Thickness` two-value constructor.** `new Thickness(1, 2)` (horizontal/vertical)
  compiles under Uno but not under the real WinUI/WinRT `Thickness` — always write all four values:
  `new Thickness(1, 2, 1, 2)`.
- **Windows has no implicit `Brush` conversion.** `Brush b = Colors.Transparent;` compiles under
  Uno (implicit conversion) but not WinUI — construct the brush explicitly, e.g.
  `new SolidColorBrush(Colors.Transparent)`.
- **Windows APIs that need an HWND to appear.** Dialogs/pickers and similar WinRT APIs must
  be associated with a window handle on Windows — use the `UnoVibe.WindowsHelper` wrapper
  instead of `WinRT.Interop` directly. The `Window` is **always non-null** at call sites (never
  pass null — it must be set or the Windows target crashes). The window flows through the
  QuickMarkup provide/inject context (see `MainPage`'s `HostWindow`).
  Folder picking routes through `WindowsHelper.PickFolderAsync(window, startPath)` — per-target
  WASDK / polyfill / classic routing — see `agents-doc/polyfills.md`.

## How to Build & Run

At the start of a new session, verify the dev server is actually running before assuming it is —
the machine may have been restarted. Check with:

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
cd /mnt/Data/Codes/UnoVibe/wt-develop # or current worktree
nohup dotnet run --project UnoVibe/UnoVibe.csproj -f net10.0-desktop --no-build -- http://localhost:4196 \
  > /mnt/LinuxProgramData/tmp/opencode/app_run.log 2>&1 & disown

# Relaunch (kill only the app; leave servers alone)
pkill -9 -x UnoVibe
```

### Positional argument

The app takes a **single positional argument** (VSCode `code path` style):
- A folder path runs `opencode serve` there.
- An `http(s)://` URL connects to an existing server.
- With no argument it shows `ConnectPage` (connect to existing server, or pick a folder and run
  `opencode serve` there).

Optional `--password [value]` overrides the default password behavior (folder: generated strong
password; server: no password):
- A bare `--password` uses `OPENCODE_SERVER_PASSWORD`.
- `--password ""` means no password.
- `--password <value>` uses the given password.
- A folder path that resolves to a file fails the launch (error + exit 1); a missing folder is
  created.

### Shell safety

Note: `pkill -f "opencode serve ..."` and similar broad patterns can hang the shell session in this
environment — prefer `pkill -9 -x <exact-name>` or `pkill -f` with a unique exact port, and check
with `ps aux | grep "opencode serve"` afterward.

Do **not** run `find /` (filesystem-wide searches) — they are extremely slow and time out the
shell. Use targeted `find` under a specific directory (e.g. `find ~ -name recent.json`), the
Glob/Grep tools, or known absolute paths.

### CRITICAL — do NOT kill the dev server

**do NOT kill the dev `opencode serve` (port 4196) during this chat session:**
this opencode session itself is served by that instance, so killing it terminates the chat and the
command. If a server-side change (e.g. `small_model`/auth/setting edits in
`~/.config/opencode/opencode.jsonc`, global opencode.json, or plugins) requires a restart to take
effect, ask the user to restart it themselves rather than running `pkill -9 -x opencode`
(or dropping into the session's own TUI to `/session` restart).

### CRITICAL — do NOT kill, relaunch, or auto-test the app

**do NOT kill, relaunch, or auto-test the UnoVibe app:**
the user is talking to this opencode session **through the running UnoVibe app**, so killing it
(`pkill -9 -x UnoVibe` or otherwise) terminates the user's view of the chat.

- Do **not** launch the app yourself.
- Do **not** test it yourself via app-mcp (`uno_app_start`, `uno_app_visualtree_snapshot`,
  `uno_app_get_screenshot`, pointer/key input, etc.).
- After making changes, **return to the user**: summarize what changed and state clearly what the
  user should test manually (e.g. "launch/relaunch the app and check X").
- Only use app-mcp to investigate when the **user explicitly asks you to**
  (e.g. "inspect the UI", "take a screenshot", "why is X not rendering").

## Source Layout

The directory tree under `UnoVibe/` is the source of truth — read it instead of a list here.
Non-obvious ownership only:
- `ConnectPage` owns the connect flow (serve launch, URL connect, password resolution).
- `SettingsPage` is only hosted by `MainPage`, as its modal overlay.
- `UnoVibe/Pages/Test/` is a dev-only harness gated by `UNOVIBE_TEST_PAGE=1`.

## QuickMarkup

**Always load the skill** when editing QuickMarkup UI:
`.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo).

## Referenced / Cloned Projects

The upstream source checkouts (QuickMarkup, Uno, opencode) exist only on the Linux dev machine —
a Windows dev environment does **not** have them, so don't assume those paths are available there.
See `agents-doc/referenced-projects.md` for the paths, what each is for, and the Uno TextBox
key-processing quirk SuggestBox works around.

## Architecture decisions

- **Single source of truth.** Only one place holds a piece of data, unless a cache is
  consciously agreed upon. Multiple writers drift out of sync.
- **State isolation on directory/session change.** When the selected directory or session
  changes, use a different, new object rather than resetting the existing one,
  so no stale state leaks across.
- **Null means absent.** Reactive model fields use `string?` with `null` for absent —
  never `""`. See `agents-doc/tool-views.md` for the convention detail.

## CONTRIBUTION RULES AND BANNED PATTERNS

This applies to new and changed codes.

### `ConnectionStatus` message is not for error.

Don't set error message to `ConnectionStatus` for failure. Its rendering is too small and
user can't read it. It's just have enough space for `Connected` string.

Instead: show an in-app toast — `Toasts.ShowError(message, title)` for failures,
`Toasts.ShowWarning(message, title)` for transient notices, or `Toasts.ShowToast(new ToastItem { ... })`
for full control (variant/duration). Sole exception: connect-time failures inside `ConnectAsync`
still write `ConnectionStatus`, which `ConnectPage` displays on its own status line (no toast host
exists until `MainPage` mounts).

### No fire-and-forget async (`_ =`, `async void` method).

Avoid discard a `Task` with `_ =` and avoid making `async void` methods.
Unobserved exceptions silently disappear. Await the call if intended
to wait or wrap the call in `AsyncHelper.RunAndReport(task, Toasts, error, title)`
if able so failures surface as toasts.

### No blanket "refresh all" methods.

Avoid methods that iterate every known item to re-fetch from the server (e.g.
`RefreshBranches()` that loops all directories). They are expensive and a maintenance
footgun — callers forget they exist and invoke them at the wrong time. Prefer targeted
per-item refresh that is called explicitly when the item is created or an event arrives.

### UI contribution (new code)

- Use native WinUI styling as much as possible.
- Avoid introducing new hex codes or custom colors unless strictly necessary.
- Do not style interactive components (Buttons, toggles, etc.) with Card background or
  Card stroke unless explicitly requested or required for the task. This deviates from
  native WinUI styling.
- Apply styling only when strictly required, not for visual preference. The goal is to
  maintain the native WinUI look.
  - OK: Set a transparent background on a Button when it is not selected.
  - Not OK: Apply Card background to a Button as its default state (visually similar but
    adds unnecessary code).
