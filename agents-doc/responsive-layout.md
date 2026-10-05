# Responsive / small-screen layout (MainPage / ChatPage)

Reference for the compact-window layout system.
**Read this file when** changing page grid layouts, compact breakpoints, or how the sidebar/chat
views switch on narrow windows. (ConnectPage has its own compact mode — see
[`connect-page.md`](connect-page.md).)

On small windows the sidebar and chat can't both fit, so they become **two full-width views**
switched by a flag; wide windows keep the side-by-side layout and ignore the flag.
The single source of truth is `MainPage` (the root page, so it sees the whole window width):
- `MainPage` provides `IsCompact` and `IsSidebarView` flags. A size-changed handler sets
  `IsCompact` at the compact breakpoint, and **resets the view to chat** whenever compact is
  entered/left, so a resize starts from the chat view.
- Layout: computed column widths + visibilities. Wide → sidebar fixed + chat star, both visible.
  Compact → one full-width panel at a time, the other collapsed.
  Both panels stay **mounted** (just Collapsed), so chat scroll/input state survives view switches.
- **Switching views** (all via the shared injected flag):
  - The chat header shows a hamburger when compact → sidebar view.
  - The sidebar shows a "Back to chat" button when compact → chat view;
    tapping a session also returns to chat after switching.
  - Creating a session (group "+", Open Folder) returns to chat.
- The chat sub-components optionally inject `IsCompact` and share the **same** reference via the
  provide/inject context chain, so one resize reflows the whole window: the header moves its
  summary to a second line instead of hiding it, and the composer hides labels and narrows
  combos (the picker row stays horizontal — Uno's `WrapPanel` has no spacing there).