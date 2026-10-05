# Markdown rendering

Reference for how markdown becomes UI in `UnoVibe/Controls/MarkdownView.cs`.
**Read this file when** editing `MarkdownView`, `MessageTextPart`, `CodeHighlighter`,
`AccentPalette`, or anything that renders message text or reasoning summaries.

`MarkdownView` in `UnoVibe/Controls/MarkdownView.cs`, powered by the `Markdig` package.

**Default behavior:**
- Assistant text parts render markdown by default.
- User text parts default to plain accent-bubble `TextBlock`s but can be toggled to markdown too
  (see `MessageView.cs` text-part branch).

There is **no RichTextBlock on Uno** (it's a `[Uno.NotImplemented]` stub) and no WCT markdown
component, so `MarkdownView` renders each Markdig block as its own stacked element and inline markup
as `Run`/`LineBreak`/`Hyperlink` in a TextBlock's `Inlines`.

**Contiguous flow blocks (paragraphs + headings) are merged into a single TextBlock** so the
user can select text across multiple lines/paragraphs at once; headings keep their size/weight
via per-run styling. Code/quote/list/table/hr stay separate elements (borders/backgrounds need
them).

**Streaming model:**
`Text` is the reactive markdown source; on each delta the component re-parses the whole string
then reconciles the rendered block stack by content key, keeping elements with unchanged keys
and rebuilding from the first divergent block — so appending to the tail rebuilds only the last
element. Markdig natively handles unfinished input (open fences stay code blocks, unclosed
inline markers stay literal), matching the web client's streaming "heal".

**PlainMode:**
a `bool` reference switching to a raw-text `TextBlock`; the **toggle UI lives outside
the component** — the per-text-part bubble owns the border plus the action row (markdown/plain
toggle, and the ↶ undo button for user messages), defaulting user → plain, assistant →
markdown, so toggling is scoped to just that bubble.

**Reasoning blocks** render their summary body through `MarkdownView` too — markdown by default
with the same toggle, shown only while expanded.

**Deliberate simplifications for the prototype:**
- HTML blocks render as raw source in a code-style box.
- Tables render as a real Grid (star columns, header styling, gridlines, alignment, spans;
  invalid tables fall back to raw source).
- **Fenced code blocks get ColorCode syntax highlighting.** The language comes from the fence
  info string (aliases work); blocks with no info or no grammar render plain. Theme (dark vs
  light) is detected per colorize and picks a cached palette; code blocks use the configured
  **Code font** setting (see [`settings.md`](settings.md)).
  **Live re-theming:** brushes are baked into elements at build time, so views re-render on theme
  change — without this, blocks rendered before a flip keep stale colors.
- Inline code is tinted with the **secondary accent** (hue-shifted teal family) to stay distinct
  from accent-colored links.
- `Hyperlink` only for absolute URLs (email autolinks get a `mailto:`-prefixed Uri so
  they navigate).

Reuse in another QuickMarkup project: copy this file + `AppSymbolIcon.cs` + `CodeHighlighter.cs`
and add the Markdig + ColorCode.Core packages.