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
`MessageJsonHelper` normalizes every mapped string through `NullIfBlank`.
Readers therefore use a plain `is not null` check. The friendly tool-name labels live in exactly
one place (`ToolDisplayName`/`TitleOrDisplay`).

## apply_patch rendering

OpenAI-style models sometimes emit `apply_patch` (a single `patchText` with add/update/delete ops)
instead of `edit`/`write`. The tool returns `{ metadata: { diff, files, diagnostics } }` where `files`
is a per-file list
`{ filePath, relativePath, type: "add"|"update"|"delete"|"move", patch, additions, deletions, movePath }`
(source `packages/opencode/src/tool/apply_patch.ts`), landing in the tool part's `state.metadata`.

`ApplyToolState` captures both `metadata.diff` and `metadata.files` (→ `PatchJson`);
`MessageView` dispatches `tool == "apply_patch"` to `ToolViewPatch` — a collapsible card that
parses the per-file list and renders one bordered block per file with a TUI-style label plus
`(+N -M)` counts. Each non-delete file's patch renders through `DiffView` (see below); delete
files show a summary instead (TUI parity). Falls back to the raw `Part.Diff` when the server
omits per-file metadata.

Mirrors the TUI's `ApplyPatch` and the web client's `patch` renderer.

## Accordion headers

`AccordionHeader` is the shared collapsible-section header used by the reasoning view and the
`edit`/`write`/`apply_patch` tool cards. It owns the chevron, the spinner, and the expanded
display state; hover and pressed visuals come from the native `Button` style. Each caller
keeps its own `bool Expanded` and flips it via a toggle callback.

- Props: `Title` (required), `Expanded`, `Enabled` (default true), plus styling flags.
- When `Enabled` is false the header renders as a plain row with no click target — never a
  greyed-out disabled `Button`.
- Tool cards pass the busy flag with pending/running foregrounds.

## Tool diff / code views

`DiffView` and `CodeView` are self-contained QuickMarkup controls that render colored code into
one selectable `TextBlock` (no RichTextBlock on Uno), used by the `edit`/`apply_patch`/`write`
tool cards.

- **`DiffView`** renders a unified diff with per-line coloring in a single TextBlock: hunk headers
  accent + bold, added lines success, removed lines critical, file metadata tertiary, context
  primary. A muted old/new **gutter** column is derived from each hunk header's range so line
  numbers track the file columns like git's diff output. Long diffs collapse with a "Show more"
  toggle, and a trailing muted `…` marks the truncation.
- **`CodeView`** renders a `write` tool's content as line-numbered, syntax-highlighted code. The
  whole source is colorized once (so multi-line strings/comments span lines) and the runs are
  split at line boundaries to interleave a muted gutter. The language is resolved from the file
  path; unknown extensions render plain with line numbers. Same collapse behavior as DiffView.
- Both re-render reactively on streaming deltas.
- `ToolViewEdit` shows the diff (plus raw output when present); `ToolViewPatch` uses one
  `DiffView` per file patch; `ToolViewWrite` shows the content in a `CodeView` (falling back to
  truncated output when older servers omit content).