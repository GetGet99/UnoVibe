# Session state (client-side behaviors)

Reference for the per-session, client-side state and behaviors that drive the chat page.
**Read this file when** editing chat send/revert/fork/autoscroll behavior, `ChatboxState`, or
`SessionsStateProvider`. The server protocol behind these features is in
[`opencode-server.md`](opencode-server.md); the sidebar model is in
[`session-sidebar.md`](session-sidebar.md).

> **Provider/State model:** `SessionsStateProvider` manages session lifecycle (creation,
> switching, directory changes, sidebar reconciliation). Per-session chat behavior lives on
> `ChatboxState` (send/queue/auto-continue/commands), reached via
> `SessionsStateProvider.Chatbox(sessionId)`. `ChatPage` re-hooks the active session's state
> on session switch.

## Send bumps sidebar order

Every user-initiated transmission (`SendPromptNowAsync` with `isUserSend`, plus the
fire-and-forget shell/command dispatches and local create/fork) bumps the session's sidebar
`SortKey` to now — see "Sidebar ordering" in [`session-sidebar.md`](session-sidebar.md).
Queued-prompt drains bump (they carry user-typed content); silent auto-continue loops pass
`isUserSend: false` and never reorder.

## Interrupt / send-while-busy

Interrupting calls `POST /session/:id/abort` (the server cancels the runner + in-flight tools
and marks the assistant message aborted).

The **send-mode setting** (`send.mode`, "Send message default" in Settings) decides what
a send does while a turn is running:
- **On next tool call** (default): send immediately — the server serializes it itself and the
  running loop picks it up at the next agent step. Matches the TUI.
- **Queue**: hold the prompt in a client-side per-session queue (surfaced as the `⏳ N queued`
  badge) and flush one at a time when the session goes idle. The queue survives session
  switches; queued prompts drain in the background when that session idles.
- **Send immediately**: interrupt the running turn first, then send — the new prompt becomes
  the active request. When idle it sends like "On next tool call". (UnoVibe-only; the TUI/web
  have no interrupt+send flow.)

**Busy-state send button:** while a turn runs, the composer's send button becomes a split button —
the primary click sends with the configured mode, and the chevron opens the three modes as
**one-time overrides** (they never change the setting).

## Shell mode ("!" prefix)

Typing `!` as the entire composer input flips the composer into **shell mode**: the trigger
character is stripped, the input gets an accent border + shell placeholder, and the mode/model row
is replaced by a hint line. Detection watches text changes rather than keys. Esc, the cancel
button, or submitting leaves the mode. While active, `/`+`@` suggestion prefixes and image attach
are disabled, and revert/fork restores force-exit the mode.

Submit fires `POST /session/{id}/shell` detached on a dedicated no-timeout client; the endpoint
blocks until the command exits and all progress arrives over SSE (the server records a synthetic
user message, auto-hidden by the existing synthetic-part logic, plus an assistant message whose
running `bash` tool part streams output). The session goes busy for the duration, so Stop aborts
the command.

## Turn-stop handling: Continue button + auto-continue

When a turn stops, the client decides between showing the end-of-chat **⟳ Continue** button
(clicking it sends the literal prompt `continue`) and, when **Auto-continue on thinking stop**
is enabled and the chat ends on an unfinished Thinking part, firing that same continue
automatically. Stop signals (`session.status idle` and/or the final `message.updated`) arrive in
either order and are handled uniformly; echoes of an already-auto-continued stop are ignored until
the server confirms the restarted turn. The auto-fired continue is silent (no toast, no sidebar
indicator). A streak cap of 10 consecutive auto-continues hands control back to the manual button
as a runaway-loop guard. Aborted turns never qualify. A client-side interrupt flag suppresses
auto-continue even when the aborted marker hasn't arrived yet.

## Idle reconciliation for busy tools

`message.part.updated` is the live path for tool completion, but a missed event leaves a card
stuck busy (e.g. patch on "Preparing..." with a yellow spinner after the turn already ended).
The miss happens on session open: events arriving between the initial `GET message` response
and the event subscription are never delivered, so a tool completing in that window stays
busy locally. `ChatMessagesState` therefore runs the same repair in two places, both
targeted to a single session (never a blanket refresh):
- Right after the initial load, once events are subscribed, it refetches and re-applies any
  server-side `tool` part over a locally-busy `ToolCallPartItem`.
- When `session.status idle` arrives, it repeats the check (no-op when none are busy).

## Revert / undo

**API:**
- `POST /session/:id/revert` with `{"messageID":"msg_..."}` (409 when busy — abort first).
- `POST /session/:id/unrevert` with `{}` (400 when no revert).

`revert.messageID` = the user message the conversation is rewound to; the server **keeps**
reverted messages until the next prompt, when cleanup removes messages with
`id >= revert.messageID` (emitting `message.removed`) and clears the marker.

Reverting restores the undone prompt (text + re-staged image attachments) into the composer.
The `/undo` and `/redo` built-in commands (see [`suggest-box.md`](suggest-box.md)) are the UI
callers, plus the per-message ↶ revert flyout, which rewinds to that exact user message.

**Per-message revert to a specific message:**
every user message renders a small always-visible **↶ revert icon** in an action row under its
text bubble. Clicking it opens a **confirmation flyout** (light-dismissed, no Cancel button).
The action row is deliberately **not hover-revealed** — toggled Visibility would reflow the message
and fight the stick-to-bottom autoscroll; future actions (fork etc.) go in the same row.

**No message refetch after undo/redo** (the TUI/web don't do one either):
the server keeps reverted messages until the next prompt, so the local list is already
authoritative and the revert point is just toggled.
The chat page hides messages at/after the revert point (a per-item conditional — NOT a physical
removal, so Redo can restore from the still-cached list) and renders a "N message(s) reverted"
card with a Redo button (Redo = revert forward to the next user message, or `unrevert` when none).
The message list is keyed by message id so elements are reused across collection resets.
Message ids (`msg_...`) are lexicographically sortable — compare with ordinal string comparison,
never parse.

## Image attachments

The ChatPage attach (camera) button opens `Windows.Storage.Pickers.FileOpenPicker` (XDG portal on
Linux; `FileTypeFilter` `.png/.jpg/.jpeg/.gif/.webp/.bmp`) and stages `ImageAttachment`s into
`ChatboxMessage.Images`, shown as a thumbnail strip (Row 3) with ✕ remove buttons;
`PendingImageCount` drives strip visibility.

On send, the client builds prompt parts from the text (omitted if whitespace-only) plus one
file part per pending image (data-URL), then clears the strip.
Sent/echoed image parts render as a thumbnail bubble. Deliberately **no** model
`capabilities.attachment` check at attach time (mirrors TUI/web);
guarding image-incapable models is a known follow-up.

## Fork conversation

**API:**
- `POST /session/:id/fork` with body `{}` (full-session fork) or
  `{"messageID":"msg_..."}` (fork at a message) returns the new session's `Session.Info`.
- The server copies every message with `id < messageID` (the forked-at message itself is
  **excluded**; message ids are re-mapped) into a new session
  titled `"<original title> (fork #N)"`.
- Forked sessions get **no `parentID`** (only `task` subagents do), so they appear as normal root
  sessions in the sidebar with no back button; `session.created` is emitted so the sidebar picks
  it up.

**UnoVibe UI:**
every user message renders a **⇆ fork icon** in the action row next to the ↶ revert icon.
Unlike revert there's **no confirmation flyout** (fork is non-destructive — it creates a new session).
Forking switches to the new session, then restores the forked-at message's prompt into the
composer (plus re-staged attachments) — the user edits/continues from there, matching the
TUI/web fork-navigate-with-prompt flow.

**Full-session fork** (no message id) is available from a **⇆ button in the chat header row**
(tooltip "Fork full session", disabled until a session exists) — same flow but with no composer
restore (the whole conversation is copied, nothing to re-inject).

Note: the fork-point message itself is excluded from the new session, so the composer prompt is what
re-injects it. The client falls back to `GET /session/:id` when the fork isn't yet in the
sidebar list (race with `session.created`).

## Chat autoscroll (stick-to-bottom)

The page tracks a stick-to-bottom flag, updated on every scroll-view change: near the bottom ⇒
pinned, anything above ⇒ unpinned.

Follow-the-stream scrolling is driven **only** by scroll-content size changes (fires **after** the
frame's layout pass, so the viewport never jumps). It covers new messages, in-place streaming
deltas, and toggling a collapsed Thinking header.

All scroll triggers only run while pinned:
- A manual scroll-up disables autoscroll.
- Scrolling back down to the bottom re-enables it — never before the bottom is hit.
- Explicit app actions (send, continue, undo/redo, permission card) force-scroll to bottom
  to re-pin regardless of position.
- A session switch/new session also re-pins.