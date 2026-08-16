# Responsive / small-screen layout (MainPage / ChatPage)

Reference for the compact-window layout system.
**Read this file when** changing page grid layouts, compact breakpoints, or how the sidebar/chat
views switch on narrow windows. (ConnectPage has its own compact mode — see
[`connect-page.md`](connect-page.md).)

On small windows the sidebar and chat can't both fit, so they become **two full-width views**
switched by a flag; wide windows keep the side-by-side layout and ignore the flag.
The single source of truth is `MainPage` (the root page, so it sees the whole window width):
- `MainPage` declares `provide bool IsCompact = false;` and `provide bool IsSidebarView = false;`.
  `OnRootSizeChanged` (a `SizeChanged` handler attached from its `[QuickMarkupConstructor]` Ctor)
  sets `IsCompact` when the width crosses `CompactBreakpoint = 820`, and **resets `IsSidebarView`
  to false** whenever it enters/leaves compact, so a resize starts from the chat view.
- Layout: computed `SidebarColumnWidth`/`ChatColumnWidth` (GridLength) + `SidebarVisibility`/
  `ChatVisibility`. Wide → sidebar 280 + chat star, both visible. Compact → `IsSidebarView` true:
  sidebar full-width star + chat Collapsed/0; false: chat full-width star + sidebar Collapsed/0.
  Both panels stay **mounted** (just Collapsed), so chat scroll/input state survives view switches.
- **Switching views** (all via the shared injected `Reference<bool>`):
  - `ChatHeader` shows a hamburger (`Symbol.GlobalNavButton`, glyph 0xE700, added in
    `SymbolExtemsion.cs`) when compact → `IsSidebarView = true`.
  - `SessionSidebar` shows a "Back to chat" button when compact → `IsSidebarView = false`;
    tapping a session also returns to chat after `SwitchSessionAsync`.
  - `FolderActions.OnNewSession` (group "+") and `SessionSidebar.OpenFolderAndStartSessionAsync`
    return to chat after creating a session.
- The chat sub-components `inject? bool IsCompact;` (optional — defaults to false/desktop when the
  provider is absent) and get the **same** `Reference<bool>` via the provide/inject context chain
  (ChatPage → MainPage), so one resize reflows the whole window:
  - `ChatHeader`: on compact the inline cost/tokens/ctx summary moves to a second header line
    (costs/context stay visible) instead of hiding; shrinks the horizontal padding/spacing.
    The title row is a Grid whose title star-column truncates with an ellipsis
    (`TextTrimming.CharacterEllipsis`) while the pencil/edit button keeps its Auto column.
  - `ChatComposer`: hides the Mode/Model/Variant labels, narrows the mode/variant combos
    (MinWidth 90 → 76) and the `ModelPicker` (MinWidth 200 → 120), and tightens paddings/spacing.
    The picker row stays a horizontal `StackPanel` (Uno's `WrapPanel` here has no `Spacing`/`Padding`).
  - `ChatStatusArea`: shrinks the horizontal padding to match the header/composer.