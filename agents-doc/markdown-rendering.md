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

**Contiguous flow blocks (paragraphs + headings) are merged into a single TextBlock joined by
`LineBreak` inlines** so the user can select text across multiple lines/paragraphs at once (each block
would otherwise be a separate TextBlock, breaking cross-block selection); headings keep their
size/weight via per-run `FontSize`/`SemiBold` on the `InlineStyle` record.
Code/quote/list/table/hr stay separate elements (borders/backgrounds need them).

**Streaming model:**
`Text` is the reactive markdown source; on each delta the component re-parses the whole string
(Markdig is fast — ~8.5 GB/s, ~96µs for a typical message) then reconciles the rendered block stack
by content key (`flow:FNV` for a merged-flow span, `BlockKind + FNV` otherwise; spans split on
`Block.Line`), keeping elements with unchanged keys and rebuilding from the first divergent block —
so appending to the tail rebuilds only the last element.
Markdig natively handles unfinished input (open fence stays a code block via
`FencedCodeBlock.ClosingFencedCharCount == 0`; unclosed inline markers stay literal), matching the
web client's streaming "heal".

**PlainMode:**
`PlainMode` (a `bool` reference) switches to a raw-text `TextBlock`; the **toggle UI lives outside
the component** — `MessageTextPart` (in `UnoVibe/Pages/Chat/MessageTextPart.cs`) owns the per-text-part
bubble: it renders the accent/card `Border` around a `MarkdownView` plus a per-part action row
(markdown/plain bullets↔Aa toggle for both roles, and the ↶ undo button for user messages), and keeps
its own internal `PlainMode` (defaulted in its ctor: user → plain, assistant → markdown) so toggling
is scoped to just that bubble.
The bubble + action row align right for user messages and left for assistant messages.
Both roles render text parts through `MarkdownView`; user bubbles keep one consistent look in both
states — a low-alpha accent tint (`new SolidColorBrush(accent.Color with { A = 25 })`, reactive on
`theme.Accent`) + 1px CardStroke, so the bubble is distinguishable from the full-accent hyperlink
color.

**Reasoning blocks** (`ToolViewReasoning`, expanded state) render their summary body through
`MarkdownView` too — markdown by default with the same bullets↔Aa toggle, shown only while expanded.

**Deliberate simplifications for the prototype:**
- HTML blocks render as raw source in a code-style box (content from `HtmlBlock.Lines`, not source
  spans — a span-slicing edge produced empty boxes).
- Tables render as a real Grid (star columns honoring `TableColumnDefinition.Width`, header row
  SubtleFill + SemiBold, per-cell DividerStroke gridlines, column alignment, ColumnSpan/RowSpan via
  `Grid.SetColumnSpan`/`SetRowSpan`, invalid/zero-column tables fall back to raw source).
- **Fenced code blocks get ColorCode syntax highlighting.** `RenderCode` asks
  `UnoVibe/Controls/CodeHighlighter.cs` to colorize the block's text (`ColorCode.Core` —
  a `TextBlockFormatter : CodeColorizerBase` emits styled `Run`s into the code `TextBlock`'s
  `Inlines`, flattened from the scope tree via a `List<Scope>` stack; `EffectiveStyle` returns the
  innermost scope with a style entry, fixing ColorCode's own "previous scope" quirk). The language
  comes from `FencedCodeBlock.Info` (trimmed) via `Languages.FindById`, so alias fences work
  (`ts`→typescript, `csharp`→c#, `sh`→bash, `py`→python); indented code blocks have no `Info`, and
  languages with no ColorCode grammar render as plain text. Theme (dark vs light) is detected once
  per colorize from the target element's resolved `ActualTheme` (`CodeHighlighter.IsDarkTheme`,
  falling back to the `UISettings` background-brightness poll when no element is given) and picks
  a cached `StyleDictionary.DefaultDark`/`DefaultLight`; brushes come from a `BrushFromHex`
  `SolidColorBrush` cache (`ColorCode.Styling.Style` is aliased — `Style` clashes with
  `Microsoft.UI.Xaml.Style`). Code blocks use the configured **Code font** setting
  (`SettingsStore.CodeFont`, resolved by `Services/CodeFonts.cs` — see
  [`settings.md`](settings.md)).
  **Live re-theming:** brushes are baked into elements at build time, so both `MarkdownView`
  and `CodeView` re-render on their root element's `ActualThemeChanged`: `MarkdownView` carries
  the dark/light flag in every reconcile key (`d:`/`l:` prefix), so the flip rebuilds all blocks;
  `CodeView` clears and refills its TextBlock's inlines via `FillInlines`. Without this,
  blocks rendered before a flip keep stale colors — notably ColorCode's light palette renders
  black text on the dark background (the Linux startup order makes this visible: Uno's
  X11 theme helper reports Light until the async DBus portal read resolves).
  The same class also exposes the lower-level building blocks for reuse: `ResolveLanguageFromPath`
  (extension → ColorCode language, mirroring the TUI's `util/filetype.ts` subset), `ColorizeRuns`
  (the styled fragment list — lets `CodeView` interleave line numbers) and `ToRun` (fragment →
  `Run`).
- Inline code (`CodeInline`) is tinted with the **secondary accent**
  (`AccentPalette.InlineCodeBrush` — the primary accent hue-rotated −40° into a teal family,
  brightness-shifted toward the theme background: `Light2` in dark themes, `Dark2` in light).
  This keeps `code` visually distinct from accent-colored links. The shared palette service lives
  in `UnoVibe/AccentPalette.cs` (hue shift + WinUI light/dark variants) for reuse.
- `Hyperlink` only for absolute URLs (email autolinks `<a@b.c>` get a `mailto:`-prefixed Uri so
  they navigate).

Reuse in another QuickMarkup project: copy this file + `AppSymbolIcon.cs` + `CodeHighlighter.cs`
and add the Markdig + ColorCode.Core packages.