# Markdown rendering

Reference for how markdown becomes UI.
**Read this file when** editing message-text rendering.

## Product decisions

There is **no RichTextBlock on Uno** (it's a `[Uno.NotImplemented]` stub) and no WCT markdown
component, so `MarkdownView` renders each Markdig block as its own stacked element and inline
markup as runs in a TextBlock.

- **Contiguous flow blocks (paragraphs + headings) are merged into a single TextBlock** so the
  user can select text across multiple lines/paragraphs at once; headings keep their size/weight
  via per-run styling. Code/quote/list/table/hr stay separate elements (borders/backgrounds
  need them).
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
