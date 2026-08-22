# Session state (client-side behaviors)

Reference for the per-session, client-side state and behaviors that drive the chat page.
**Read this file when** editing send/interrupt logic, `SessionStore`, revert/undo, image attachments,
fork, or chat autoscroll. The server protocol behind these features is in
[`opencode-server.md`](opencode-server.md); the sidebar model is in
[`session-sidebar.md`](session-sidebar.md).

> **Store split (router + per-session stores):** feature notes predate the split and name `ChatStore`
> for everything. Today `ChatStore` is the router (connection, sidebar, shared options, permissions,
> toasts, the `SessionStore` cache, and the `Active` re-point). Anything per-session — messages,
> composer mode/model/variant, usage/context stats, revert/redo, retry card, pending images,
> `SendAsync`/`RenameSessionAsync`/`SetMode`/`SetModel`/`SetVariant` — lives on the cached
> `SessionStore` and is reached via `Store.Active.X` (or `Store.Active.X(...)` from `ChatPage`
> code-behind). `ChatPage` re-hooks the active store's message list on the router's
> `ActiveStoreChanged` event.

## Interrupt / send-while-busy

`ChatStore.InterruptAsync()` calls `POST /session/:id/abort` (the server cancels the runner +
in-flight tools and marks aborted tool parts with `state.metadata.interrupted=true` and the assistant
message `error.name === "MessageAbortedError"`).

## Send while busy

The **send-mode setting** (`SettingsStore.SendMode`, "Send message default" in Settings) decides what
a send does while a turn is running:
- **On next tool call** (default): fire `prompt_async` immediately — the server serializes it itself.
  `createUserMessage` stores the prompt at once; the running session loop picks it up at the
  **next agent step** (after the in-flight tool call), not at full idle. Matches the TUI
  (`stream.transport.ts` `runPromptTurn` calls `promptAsync` regardless of busy; its `state.wait`
  gate only prevents a second concurrent UI submit).
- **Queue**: `SessionStore.SendAsync` holds the prompt in the client-side queue
  (`EnqueuePrompt`/`DrainPendingPromptsAsync`, surfaced as the `⏳ N queued` badge) and flushes it
  one at a time when the session goes idle (`OnTurnCompleted` / `ApplySessionStatus`). The queue is
  per-`SessionStore` (so per cached session) and survives session switches; queued prompts drain in
  the background when that session idles.
- **Send immediately**: `SessionStore.SendAsync` interrupts the running turn first
  (`InterruptAsync` → `POST /session/:id/abort`) then fires `prompt_async`, so the new message
  becomes the active request instead of waiting for the next agent step. The abort POST returns once
  the runner is idle, so the following prompt starts a fresh turn. When idle it sends like
  "On next tool call". (Verified sound against the server: `prompt` → `createUserMessage` then
  `loop` → `Runner.ensureRunning`, which starts a fresh run from `Idle`; the TUI/web have no
  interrupt+send flow — this is UnoVibe-only.)

**Busy-state send button:** while a turn runs, the composer's send button becomes a `SplitButton`
(`Controls/SendModeButton.cs`) — the primary click sends with the configured `SendMode`, and the
chevron opens a `MenuFlyout` of the three modes as **one-time overrides** (they never change the
`send.mode` setting; the primary stays the configured default). The menu checkmark + the button
tooltip track the setting live: `ChatPage` keeps the reactive `SendMode` ref synced via
`SettingsStore.Changed` (bounced to the UI thread) and passes it as the component's `Mode` prop;
the component reads `SettingsStore.SendMode` fresh for the primary click. When idle the send button
is a plain button that sends immediately.

## Turn-stop handling: Continue button + auto-continue

When a turn stops, `SessionStore` decides between showing the end-of-chat **⟳ Continue** button
(`ShowContinue`, rendered by `ChatMessageList`; clicking it sends the literal prompt `continue`)
and, when **Auto-continue on thinking stop** (`turn.autocontinue`,
[`settings.md`](settings.md)) is enabled and the chat ends on an unfinished Thinking (reasoning)
part, firing that same continue automatically (`HandleStoppedTurn`). Stop signals —
`session.status idle` and/or the final `message.updated` carrying finish — arrive in either order,
are handled uniformly (`HandleStoppedTurn` from both sites), and echoes of an already-auto-continued
stop are ignored until the server confirms the restarted turn with its first non-idle status event.
The auto-fired continue is silent: no completion toast and no sidebar unread/outcome check mark
(`ChatStore.ApplySessionStatus` asks `store.WillAutoContinue()` before applying an idle event and
skips both). A streak cap of 10 consecutive auto-continues — reset by any manual send or a
non-qualifying stop — hands control back to the manual Continue button as a runaway-loop guard.
Aborted turns never qualify (a user Stop must not be answered with a continue).

## Revert / undo

**API:**
- `POST /session/:id/revert` with `{"messageID":"msg_..."}` (409 when busy — abort first).
- `POST /session/:id/unrevert` with `{}` (400 when no revert).

`revert.messageID` = the user message the conversation is rewound to; the server **keeps** reverted
messages until the next prompt, when `SessionRevert.cleanup` removes messages with
`id >= revert.messageID` (emitting `message.removed`, handled by `ChatStore.ApplyMessageRemoved`)
and clears the marker (a `session.updated` whose info omits `revert`, synced in
`ApplySessionUpsert`).

`ChatStore` holds the reactive `RevertMessageId`/`RevertCountLabel` (+ plain `RevertPromptText`)
and `RevertToMessageAsync(MessageItem)` (abort-if-busy → revert → `ApplyRevertMarker`; restores the
undone prompt (text + re-staged image attachments) into the composer via `RevertPromptText`/
`PendingImages`) plus `UndoLastMessageAsync()`/`RedoLastMessageAsync()` mirroring the TUI.
`UndoLastMessageAsync` currently has **no UI caller** (the chat-box Undo button was removed so users
scroll up to a message instead) but is kept for a planned `/undo` command — delegate new callers to
`RevertToMessageAsync`.

**Per-message revert to a specific message:**
every user message renders a small always-visible **↶ revert icon** in an action row under its text
bubble (`MessageTextPart` (per-text-part bubble + action row) → its `RevertRequested` →
`MessageView.OnPartRevertRequested` re-raises `MessageView.RevertRequested` →
`ChatPage.OnMessageRevertRequested` → `Store.RevertToMessageAsync`), which rewinds the conversation
to that exact user message (web-client per-message revert / TUI dialog-message parity).

Clicking the ↶ opens a **confirmation flyout** (`Button.Flyout` auto-opens on click — Uno calls
`OpenAssociatedFlyout()` in `Button.OnClick`, and `Click` also fires, so the outer button has NO
`@Click` handler; the flyout's "Undo" button performs the real revert and hides the flyout).
The flyout is `Placement=BottomEdgeAlignedRight` (opening it downward avoids covering the message
above being removed) with **no Cancel button** — it's light-dismissed (`ShowMode=Auto`), with an
italic "Click outside to cancel" hint.
The action row is deliberately **not hover-revealed** — toggled Visibility would reflow the message
and fight the stick-to-bottom autoscroll; future actions (fork etc.) go in the same row.

**No message refetch after undo/redo** (the TUI/web don't do one either):
the server keeps reverted messages until the next prompt, so the local `Messages` list is already
authoritative and the revert point is just toggled.
The chat page hides messages with `id >= RevertMessageId` (a per-item conditional in the message
`foreach`, reactive on `RevertMessageId` — NOT a physical removal, so Redo can restore from the
still-cached list) and renders a "N message(s) reverted" card with a Redo button (Redo = revert
forward to the next user message, or `unrevert` when none).
The message `foreach` is **keyed by `m.Id`** so QuickMarkup reuses MessageView blocks across
collection resets instead of recreating every element.
Message ids (`msg_...`) are lexicographically sortable — compare with
`StringComparer.Ordinal.Compare`, never parse.

TUI ref: `packages/tui/src/routes/session/index.tsx` undo/redo + revert-card Match blocks;
web ref: `use-session-commands.tsx` + `pages/session/timeline/model.ts`
(`selectVisibleUserMessages` keeps `id < revertMessageID`).

## Image attachments

The ChatPage attach (camera) button opens `Windows.Storage.Pickers.FileOpenPicker` (XDG portal on
Linux; `FileTypeFilter` `.png/.jpg/.jpeg/.gif/.webp/.bmp`) and stages `ImageAttachment`s into
`ChatStore.PendingImages`, shown as a thumbnail strip (Row 3) with ✕ remove buttons;
`PendingImageCount` drives strip visibility.

On send, `OpencodeClient.SendPromptAsync` builds prompt parts from the text (omitted if
whitespace-only) plus one `{type:"file", mime, filename, url:"data:<mime>;base64,..."}` per pending
image, then clears the strip.
Sent/echoed image parts render as a thumbnail bubble in `MessageView` via
`PartItem.IsImage`/`Image` (`LoadImageAsync` decodes the data URL fire-and-forget; non-image `file`
parts keep the old `file: <name>` line).
Deliberately **no** model `capabilities.attachment` check at attach time (mirrors TUI/web);
guarding image-incapable models is a known follow-up.

## Fork conversation

**API:**
- `POST /session/:id/fork` with body `{}` (full-session fork) or
  `{"messageID":"msg_..."}` (fork at a message) returns the new session's `Session.Info`.
- The server copies every message with `id < messageID` (the forked-at message itself is
  **excluded**; message ids are re-mapped, compaction `tail_start_id`s rewritten) into a new session
  titled `"<original title> (fork #N)"` (`Session.fork` in `session.ts`; fork input
  `{ sessionID, messageID? }`).
- Forked sessions get **no `parentID`** (only `task` subagents do), so they appear as normal root
  sessions in the sidebar with no back button; `session.created` is emitted so
  `ApplySessionUpsert` adds it to `Sessions`.

**UnoVibe UI:**
every user message renders a **⇆ fork icon** (WinUI `Symbol.Switch` glyph) in the action row next
to the ↶ revert icon (`MessageTextPart` → its `ForkRequested` → `MessageView.OnPartForkRequested`
re-raises `MessageView.ForkRequested` → `ChatPage.OnMessageForkRequested` →
`Store.ForkFromMessageAsync`).
Unlike revert there's **no confirmation flyout** (fork is non-destructive — it creates a new session).

`ChatStore.ForkFromMessageAsync(MessageItem)` calls `ForkSessionAsync(_sessionId, message.Id)`, then
`SwitchSessionAsync(forked.Id)` (loads the copied history, clears unread/status), then restores the
forked-at message's prompt into the composer via the plain `ForkPromptText` field (set from
`PromptTextFromMessage`) + `StageImagesFromMessage` for re-staged attachments — the user
edits/continues from there, matching the TUI/web fork-navigate-with-prompt flow.
`ForkPromptText` is reset in `ResetRevertState()` (connect/new/switch/delete).

**Full-session fork** (no message id) is available from a **⇆ button in the ChatPage header row**
(right side, next to the stats button; `Symbol.Switch` glyph, tooltip "Fork full session", disabled
until a session exists) wired to `ChatStore.ForkFullSessionAsync()` — same
`ForkSessionAsync(_sessionId)` → `SwitchSessionAsync(forked.Id)` flow but with no composer restore
(the whole conversation is copied, nothing to re-inject).

`OpencodeClient.ForkSessionAsync` uses the **legacy** `/session/:id/fork` route — the newer
`/api/session/:id/fork` HttpApi route is absent on the current dev server (1.17.18).
Note: the fork-point message itself is excluded from the new session, so the composer prompt is what
re-injects it; `SwitchSessionAsync` falls back to `GetSessionAsync` when the fork isn't yet in the
sidebar list (race with `session.created`).

## Chat autoscroll (stick-to-bottom)

`ChatPage` tracks `_stickToBottom`, updated by `scrollHost.ViewChanged` (every event, incl.
intermediate drag/inertia frames): within 40px of the bottom ⇒ pinned, anything above ⇒ unpinned.

Follow-the-stream scrolling is driven **only** by `messagePanel.SizeChanged` (the scroll content —
fires **after** the frame's layout pass, so `ScrollableHeight` is never stale and the viewport
never jumps to the top on session select). `SizeChanged` is the single trigger: it covers new
messages (collection changes resize the panel), in-place streaming deltas, and toggling a collapsed
Thinking header (which resizes the panel) — so an expanded reasoning block follows while the agent
is thinking without a redundant per-delta scroll.
(A previous `ChatStore.PartContentChanged` event fired on every `ApplyPartDelta`/`ApplyPartUpdated`;
it was removed because each scroll instantly re-pinned via `ChangeView`, killing the user's
in-progress wheel-scroll animation — the collapse/expand case proved it.)

All scroll triggers only run while pinned:
- A manual scroll-up disables autoscroll.
- Scrolling back down to the bottom re-enables it — never before the bottom is hit.
- Explicit app actions (send, continue, undo/redo, permission card) call `ForceScrollToBottom()`
  to re-pin regardless of position.
- A `Reset` on `Messages` (session switch/new session) also re-pins.

Programmatic scrolls use `ChangeView(..., disableAnimation: true)` so they raise exactly one
non-intermediate `ViewChanged` and never falsely unpin.