# Tool views and code/diff rendering

Reference for how opencode tool calls render in the chat.
**Read this file when** editing tool views or apply_patch parsing.

## Null-means-absent convention

Reactive tool-call state follows one rule: `null` means absent or invalid, and any
non-null string is meaningful content. Never store `""` or whitespace-only strings in
tool-call reactive fields.

The rule is enforced at a single choke point — the OpenCode boundary.
`MessageJsonHelper` normalizes every mapped string through `NullIfBlank`.
Readers therefore use a plain `is not null` check. The friendly tool-name labels live in exactly
one place (`ToolDisplayName`/`TitleOrDisplay`).

## apply_patch rendering

OpenAI-style models sometimes emit `apply_patch` (a single `patchText` with add/update/delete
ops) instead of `edit`/`write` (source `packages/opencode/src/tool/apply_patch.ts`, landing in
the tool part's `state.metadata`). `MessageView` dispatches `tool == "apply_patch"` to a
collapsible card rendering one bordered block per file through `DiffView`, with a summary for
delete files (TUI parity). Falls back to the raw diff when the server omits per-file metadata.

## Running metadata

`ApplyToolPart` applies `state.metadata` for `running` as well as `completed`/`error`.
This matters for `task`: the server publishes the subagent session link while the subagent is
still running (source `packages/opencode/src/tool/task.ts`), so the card can navigate to the
live subagent session before completion.
