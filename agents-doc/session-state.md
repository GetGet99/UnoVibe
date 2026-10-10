# Session state (client-side behaviors)

Reference for the per-session, client-side behaviors and why they exist.
**Read this file when** editing chat send/revert/fork/autoscroll behavior. The server protocol
behind these features is in [`opencode-server.md`](opencode-server.md); the sidebar model is in
[`session-sidebar.md`](session-sidebar.md).

> **Provider/State model:** `SessionsStateProvider` manages session lifecycle. Per-session chat
> behavior lives on `ChatboxState`, reached via `SessionsStateProvider.Chatbox(sessionId)`.

## Send bumps sidebar order

Every user-initiated transmission bumps the session's sidebar `SortKey` to now — see "Sidebar
ordering" in [`session-sidebar.md`](session-sidebar.md). Queued-prompt drains bump (they carry
user-typed content); silent auto-continue loops never reorder.

## Interrupt / send-while-busy

Interrupting calls `POST /session/:id/abort` (the server cancels the runner + in-flight tools
and marks the assistant message aborted).

The **send-mode setting** decides what a send does while a turn is running:
- **On next tool call** (default): send immediately — the server serializes it itself and the
  running loop picks it up at the next agent step. Matches the TUI.
- **Queue**: hold the prompt in a client-side per-session queue and flush one at a time when the
  session goes idle. The queue survives session switches; queued prompts drain in the background
  when that session idles.
- **Send immediately**: interrupt the running turn first, then send. When idle it sends like
  "On next tool call". (UnoVibe-only; the TUI/web have no interrupt+send flow.)

## Shell mode ("!" prefix)

Typing `!` as the entire composer input flips the composer into **shell mode**. Submit fires
`POST /session/{id}/shell` on a dedicated no-timeout client; the endpoint blocks until the
command exits and all progress arrives over SSE. The session goes busy for the duration, so Stop
aborts the command.

## Turn-stop handling: Continue button + auto-continue

When a turn stops, the client decides between showing the end-of-chat **Continue** button
(clicking it sends the literal prompt `continue` — there is **no server continue API**, matching
the TUI) and, when **Auto-continue on thinking stop** is enabled and the chat ends on an
unfinished Thinking part, firing that same continue automatically. Stop signals arrive in either
order and are handled uniformly; echoes of an already-auto-continued stop are ignored until the
server confirms the restarted turn. A streak cap of 10 consecutive auto-continues hands control
back to the manual button as a runaway-loop guard. Aborted turns never qualify.

## Idle reconciliation for busy tools

`message.part.updated` is the live path for tool completion, but a missed event leaves a card
stuck busy. The miss happens on session open: events arriving between the initial `GET message`
response and the event subscription are never delivered. The client therefore re-applies
server-side `tool` parts over locally-busy ones right after the initial load once events are
subscribed, and repeats the check when `session.status idle` arrives — both targeted to a single
session, never a blanket refresh.

## Revert / undo

`revert.messageID` = the user message the conversation is rewound to; the server **keeps**
reverted messages until the next prompt, when cleanup removes messages at/after the marker
(emitting `message.removed`) and clears the marker. Message ids are lexicographically sortable
— compare with ordinal string comparison, never parse.

**No message refetch after undo/redo** (the TUI/web don't do one either): the server keeps
reverted messages until the next prompt, so the local list is already authoritative and the
revert point is just toggled. The chat hides messages at/after the revert point without physical
removal, so Redo can restore from the still-cached list. Reverting restores the undone prompt
(text + re-staged image attachments) into the composer. The action row under each user message
is deliberately **not hover-revealed** — toggled Visibility would reflow the message and fight
the stick-to-bottom autoscroll.

## Image attachments

On send, the client builds prompt parts from the text (omitted if whitespace-only) plus one file
part per pending image (data-URL), then clears the strip.

## Fork conversation

The server copies every message before the fork point (the forked-at message itself is
**excluded**; message ids are re-mapped) into a new session. Forked sessions get **no `parentID`**
(only `task` subagents do), so they appear as normal root sessions with no back button.
Forking switches to the new session, then restores the forked-at message's prompt into the
composer — the user edits/continues from there, matching the TUI/web fork-navigate-with-prompt
flow. A **full-session fork** is available from the chat header — same flow but with no composer
restore (the whole conversation is copied, nothing to re-inject).

## Chat autoscroll (stick-to-bottom)

The page tracks a stick-to-bottom flag: near the bottom ⇒ pinned, anything above ⇒ unpinned.
Follow-the-stream scrolling is driven **only** by scroll-content size changes (fires **after**
the frame's layout pass, so the viewport never jumps). All scroll triggers only run while
pinned; explicit app actions (send, continue, undo/redo, permission card) force-scroll to bottom
to re-pin, and a session switch also re-pins.
