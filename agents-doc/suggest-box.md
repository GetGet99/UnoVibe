# Chat input suggestion box

Reference for `SuggestBox` and server command/skill routing.
**Read this file when** editing suggestions or slash-command send. For the Uno TextBox
key-processing quirk the focus management depends on, see
[`referenced-projects.md`](referenced-projects.md).

`SuggestBox` is a self-contained QuickMarkup component (multiline TextBox + attached suggestion
`Flyout`) offering `@` and `/` completions. Read and write the input only through its public
surface — never reach into the markup node directly. The flyout never steals focus, so the
editor keeps focus for the whole suggestion session. Programmatic writes never trigger a
suggestion fetch.

**Parsing:** every prefix triggers at start-of-token, so `foo /skill` works while `foo/bar` and
`foo@bar` do not. Whole-input commands are filtered out unless the trigger is at position 0.

## App built-in commands

`BuiltInCommands` is the app-level catalog (TUI parity plus UnoVibe-only ones), served locally
with no server round-trip. **Commit runs the action instead of inserting text.** Typed text that
*is* an exact built-in invocation is consumed before send routing, so it runs the action rather
than reaching the model verbatim.

**Name collisions:** built-ins win — the client skips a server command whose name matches a
built-in, mirroring how the server drops a skill whose name is taken by a command.

## Live server data

Skills appear from both the command and skill providers and are deduped by key. The `:mcp`
suffix is display-only (never inserted). There is **no mock fallback**: a null client,
unreachable server, or empty response yields an empty list and the flyout closes.

**Route skew — why the legacy routes are primary:** on the running dev server the legacy
`/command`/`/skill` routes return the FULL data (project skills, MCP entries, user commands),
while the newer `/api/*` surface returns only built-ins. The client therefore tries legacy first
and keeps `/api/*` as a fallback, accepting both a bare array and the `data` envelope.

**Slash-command send:** the server does NOT expand `/name args` inside a normal prompt, so the
client detects commands and fires `POST /session/{id}/command`. The endpoint **blocks until the
turn completes**, so the call uses a dedicated no-timeout client and is **fire-and-forget** —
progress comes entirely over SSE and the composer clears immediately. Argument parsing mirrors
the TUI (no quote/escape parsing); the server does its own quote-aware tokenizing.

**Skills expand through the same endpoint** — the server folds skills into the command list and
drops a skill whose name collides with a command, so a command always wins. The **Expand skills
via slash commands** setting (default on = TUI behavior) only affects skill-only names: with it
off such text falls through to a plain prompt.
