# Tool views and code/diff rendering

Reference for how opencode tool calls render in the chat.
**Read this file when** editing `UnoVibe/Controls/ToolViews/*`, `DiffView`, `CodeView`,
`CodeHighlighter`, or the tool-call mapping in `Helpers/MessageJsonHelper.cs`
(`ApplyToolPart`).

## Null-means-absent convention

Reactive tool-call state follows one rule: `null` means absent or invalid, and any
non-null string is meaningful content. Never store `""` or whitespace-only strings in
`ToolCallPartItem`'s reactive fields.

The rule is enforced at a single choke point — the OpenCode boundary.
`MessageJsonHelper` (`ApplyToolPart`/`ApplyToolInput`/`ApplyToolMetadata`) normalizes
every mapped string through `NullIfBlank`
(`string.IsNullOrWhiteSpace(value) ? null : value`). Readers therefore use a plain
`is not null` check in markup, and `is { Length: > 0 }` in C# only when they also need
the value bound to a variable. The friendly tool-name labels live in exactly one place:
`ToolCallPartItem.ToolDisplayName`/`TitleOrDisplay` (`ToolViewShared` delegates to them).

## apply_patch rendering

OpenAI-style models sometimes emit `apply_patch` (a single `patchText` with add/update/delete ops)
instead of `edit`/`write`. The tool returns `{ metadata: { diff, files, diagnostics } }` where `files`
is a per-file list
`{ filePath, relativePath, type: "add"|"update"|"delete"|"move", patch, additions, deletions, movePath }`
(source `packages/opencode/src/tool/apply_patch.ts`), landing in the tool part's `state.metadata`.

`ApplyToolState` captures both `metadata.diff` → `PartItem.Diff` (as before) and
`metadata.files` → `PartItem.PatchJson`; `MessageView` dispatches `tool == "apply_patch"` to
`ToolViewPatch` — a collapsible card ("← Patch <path>" / "← Patch N files", "Preparing patch..."
while in flight) that parses `PatchJson` via `ToolViewShared.ParsePatchFiles` and renders one
bordered block per file with a TUI-style label (`# Created`/`# Deleted`/`# Moved a → b`/
`← Patched <path>` + `(N+ M-)` counts). Each non-delete file's `patch` renders through
`UnoVibe/Controls/DiffView.cs` (see below); delete files show a `-N lines`
summary instead (TUI parity). Falls back to the raw `Part.Diff` via `DiffView` when the server
omits per-file metadata.

Mirrors the TUI's `ApplyPatch` (`routes/session/index.tsx`) and the web client's `patch` renderer
(`session-ui/src/components/message-part.tsx` + `apply-patch-file.ts`).

## Accordion headers

`UnoVibe/Controls/AccordionHeader.cs` is the shared collapsible-section header
(`IQuickMarkupComponent<UIElement>`) used by `MessageReasoningView` and the
`edit`/`write`/`apply_patch` tool cards. It owns the hover background, the `▸`/`▾`
chevron, the `ProgressRing` spinner, and the `Expanded` display state; each caller
keeps its own `bool Expanded` and flips it via `Toggle=`() => Expanded = !Expanded``.

- Props: `Title` (required), `Expanded`, `Enabled` (default true), `SemiBold`,
  `ShowSpinner` + `SpinnerForeground`, `TitleForeground`, `Toggle` (`Action?`).
- When `Enabled` is false the header renders as a plain `StackPanel` row with no
  background, no border, no chevron, and no click target — never a greyed-out
  disabled `Button`. `MessageReasoningView` passes `Enabled` from the summary body
  length so empty reasoning renders as plain text.
- Tool cards pass `ShowSpinner=`Part.IsBusy`` with the pending/running neutral/caution
  foreground, matching what `ToolBusyIndicator` rendered inside the old headers.

## Tool diff / code views

`UnoVibe/Controls/DiffView.cs` and `UnoVibe/Controls/CodeView.cs` are self-contained QuickMarkup
controls (`IQuickMarkupComponent<UIElement>`) that render colored code into one selectable
`TextBlock` (no RichTextBlock on Uno) and are used by the `edit`/`apply_patch`/`write` tool cards.

- **`DiffView`** renders a unified diff with per-line coloring in a single TextBlock: hunk headers
  (`@@ ... @@`) Accent + bold, added lines SystemSuccess, removed lines SystemCritical, file metadata
  (`---`/`+++`/`diff --git`/`index`/`new file`/.../`\ No newline`) TertiaryText, context PrimaryText.
  A muted old/new **gutter** column is derived from each hunk header's `-a,b +c,d` range (parsed in
  `ParseHunk`) so line numbers track the file columns like git's diff output (added lines carry the
  new number, removed lines the old, context both). Long diffs collapse via
  `ToolViewShared.CollapsePreview` (`DiffMaxLines = 60`, capped chars) with a "Show more ▾" toggle,
  and a trailing unnumbered muted `…` marks the truncation.
- **`CodeView`** renders a `write` tool's `input.content` (part field `ToolContent`, path
  `ToolFilePath`) as line-numbered, syntax-highlighted code. The whole source is colorized once via
  `CodeHighlighter.ColorizeRuns` (so multi-line strings/comments span lines) and the runs are split at
  line boundaries to interleave a muted gutter + `LineBreak` (the gutter width adapts to the line
  count). The language is resolved from the file path via
  `CodeHighlighter.ResolveLanguageFromPath`; unknown extensions render plain with line numbers.
  Same `CodeMaxLines`/`Show more` collapse as DiffView.
- Both re-render reactively via their generated `*Prop.Watch(_ => Render())` (streaming deltas) and
  clear/re-add `host.Children` on each render; the `…` marker is appended as a `Run` after the last
  `LineBreak` so it's never counted as a diff/code line.
- `ToolViewEdit` shows `Part.Diff` in a `DiffView` (plus the raw `ToolOutput` when present);
  `ToolViewPatch` uses a `DiffView` per file patch; `ToolViewWrite` shows `Part.ToolContent` in a
  `CodeView` (falling back to truncated `ToolOutput` when older servers omit content). `WriteTitle`
  counts the written file's lines from `ToolContent` (falling back to output/input).