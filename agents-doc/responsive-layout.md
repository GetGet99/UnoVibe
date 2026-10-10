# Responsive / small-screen layout (MainPage / ChatPage)

Reference for the compact-window layout system.
**Read this file when** changing page grid layouts or compact breakpoints.
(ConnectPage has its own compact mode — see [`connect-page.md`](connect-page.md).)

## Product decisions

On small windows the sidebar and chat can't both fit, so they become **two full-width views**
switched by a flag; wide windows keep the side-by-side layout and ignore the flag. The single
source of truth is `MainPage` (the root page, so it sees the whole window width): it provides
`IsCompact` and `IsSidebarView` flags, and **resets the view to chat** whenever compact is
entered/left, so a resize starts from the chat view. Both panels stay **mounted** (just
Collapsed), so chat scroll/input state survives view switches.
