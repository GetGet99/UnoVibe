# Markdown rendering

Reference for how markdown becomes UI.
**Read this file when** editing message-text rendering.

## Product decisions

Uno's `RichTextBlock` is a full implementation (BlockLayout engine, cross-paragraph selection,
hyperlinks), so `MarkdownView` renders each Markdig block as its own stacked element, with inline
markup built into blocks.

- **Contiguous flow blocks (paragraphs + headings) are merged into a single `RichTextBlock`**
  so the user can select text across multiple lines/paragraphs at once; each heading/paragraph
  becomes its own `Paragraph` (heading size/weight set on the paragraph). Code/quote/list/table/hr
  stay separate elements (borders/backgrounds need them). Inline code stays a real `Run`
  (so it is included in selection/copy) with its background painted via a `TextHighlighter`
  character range — deliberately **not** an `InlineUIContainer`, whose child text is invisible to
  `RichTextBlock.GetSelectedText`. The highlighter sets background and accent foreground together
  (Uno renders a null highlighter foreground as white). Lists use a two-column grid — bullet gutter plus content — so
  wrapped lines hang under the text, and table cells are `RichTextBlock`s. Flat-character offsets
  for highlight ranges are tracked in `InlineBuildState` (runs +1 per line break, +2 between
  paragraphs, matching Uno's layout model); when not inside a `RichTextBlock`, inline code falls
  back to a plain run.
- Streaming re-parses the whole source on each delta then reconciles by content key, keeping
  elements with unchanged keys — so appending to the tail rebuilds only the last element.
  Markdig natively handles unfinished input (open fences stay code blocks, unclosed inline
  markers stay literal), matching the web client's streaming "heal".
- Assistant text renders markdown by default; user text defaults to plain `TextBlock`s with a
  per-bubble markdown/plain toggle. Reasoning summaries render through the same view.
- **Deliberate simplifications for the prototype:** HTML blocks render as raw source in a
  code-style box; invalid tables fall back to raw source. Fenced code blocks get ColorCode
  syntax highlighting from the fence info string (blocks with no info or no grammar render
  plain), and views re-render on theme change because brushes are baked in at build time.
  Code blocks use the configured **Code font** setting (see [`settings.md`](settings.md)).
