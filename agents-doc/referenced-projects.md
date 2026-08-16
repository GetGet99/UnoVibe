# Referenced / cloned projects

Reference for the upstream source checkouts used as answers.
**Read this file when** you need upstream behavior (opencode server internals, Uno platform API
behavior, QuickMarkup syntax) or a Windows dev environment where these paths are absent.

These source checkouts exist only on the Linux dev machine — a Windows dev environment does **not**
have them cloned, so don't assume these paths (or the answers they give) are available there.

- **QuickMarkup source**: `/mnt/Data/Codes/QuickMarkup/wt-master/`
  — read this to understand markup syntax, the source generator, and what binds compile.
  Its own skill: `/mnt/Data/Codes/QuickMarkup/wt-master/.agents/skills/quickmarkup/SKILL.md`
  and `docs/qm-language.md`.
- **Uno Platform source**: `/mnt/Data/Codes/.GitHubClone/uno/`
  — useful for platform API behavior (e.g., X11 `FolderPicker` via desktop portal at `X11ApplicationHost.cs`;
  `FolderPicker.skia.cs` throws `NotSupportedException` if the extension is missing).
  Known Uno quirk (SuggestBox depends on it):
  TextBox's real key processing runs in `OnPostKeyDown` → `OnKeyDownSkia`, and `PostKeyDown` is raised
  **unconditionally** during `KeyDown` (`UIElement.RoutedEvents.cs`), so `e.Handled = true` in a
  `PreviewKeyDown` handler does NOT stop a handled Up/Down from moving the caret or a handled Enter
  from inserting a newline. SuggestBox works around it by cancelling the effects:
  `SelectionChanging` cancel (`_suppressArrowSelection`) for arrow keys while the flyout is open,
  and `BeforeTextChanging` cancel (`_blockStrayTextChange`, gated by `_programmaticTextChange`)
  for consumed Enter/Tab keys. (Used by `SuggestBox` — see
  [`suggest-box.md`](suggest-box.md)).
- **opencode source**: `/mnt/LinuxProgramData/tmp/opencode/opencode-src/`
  — server API/auth reference. Auth lives in `packages/opencode/src/server/auth.ts`.