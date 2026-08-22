using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Xaml.Documents;
using UnoVibe.Services;
using Windows.UI.Text;
using MarkdigBlock = Markdig.Syntax.Block;
using MarkdigInline = Markdig.Syntax.Inlines.Inline;

namespace UnoVibe.Controls;

/// <summary>
/// A self-contained Markdown renderer for QuickMarkup/WinUI. Renders a markdown string as a
/// vertical stack of block elements (headings, paragraphs with inline formatting, fenced/indented
/// code, lists, block quotes, thematic breaks, and raw source for tables/HTML). Inline markup
/// (bold/italic/underline, inline code, links, autolinks) is built from Markdig's inline AST into
/// TextBlock Inlines (Run / LineBreak / Hyperlink) — there is no RichTextBlock on Uno, so each block
/// is its own TextBlock stacked in a StackPanel.
///
/// Streaming-friendly: the component re-parses the full <see cref="Text"/> on change (Markdig parses
/// at ~8 GB/s, so this is microseconds) and reconciles the rendered block stack by content key,
/// reusing every element whose key is unchanged and only rebuilding from the first divergent block —
/// so appending to the tail of a message rebuilds just the last block's element. Markdig also handles
/// unfinished input correctly (an open code fence stays a code block, unclosed inline markers stay
/// literal), which matches how the web client "heals" partial markdown while a turn streams.
///
/// Self-contained and portable: to reuse in another QuickMarkup project, copy this file together with
/// <c>AppSymbolIcon.cs</c> and add the Markdig + ColorCode.Core packages. It only depends on
/// QuickMarkup + Markdig + ColorCode.Core + the WinUI types in the app's global usings.
///
/// API:
///   - <see cref="Text"/> — the markdown source. Bind reactively (e.g. `Text=`part.Text``).
///   - <see cref="PlainMode"/> — toggle between Markdown and raw-text rendering. The host owns the
///     toggle UI (UnoVibe puts a per-message button in the message action row) and flips this.
/// </summary>
[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Controls;
    string Text = "";
    bool PlainMode = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        blocksHost = <StackPanel Spacing=6 />
    </root>
    """)]
public partial class MarkdownView : IQuickMarkupComponent<UIElement>
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UsePipeTables()
        .Build();

    private readonly ThemeBrushes _theme = ThemeBrushes.Global;
    private readonly List<(string Key, UIElement Element)> _elements = new();
    private bool _themeHooked;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        TextProp.Watch(_ => Render(), immediete: true);
        PlainModeProp.Watch(_ => Render());
    }

    // ── render model ─────────────────────────────────────────────────

    private sealed record MdBlock(string Key, Func<UIElement> Factory);

    private void Render()
    {
        if (blocksHost is null) return;

        // Brushes are baked into elements at build time (runs, inline-code accent, table fills),
        // so a theme flip would otherwise leave stale colors. Hook the host's ActualThemeChanged
        // once and re-render; the per-block keys below carry the theme so the reconcile treats
        // every block as divergent and rebuilds them all with fresh brushes.
        if (!_themeHooked)
        {
            _themeHooked = true;
            blocksHost.ActualThemeChanged += (_, _) => Render();
        }

        bool dark = CodeHighlighter.IsDarkTheme(blocksHost);
        var text = Text ?? "";
        IReadOnlyList<MdBlock> blocks = PlainMode
            ? new[] { new MdBlock($"{ThemeKey(dark)}plain:{Fnv(text)}", () => PlainTextBlock(text)) }
            : BuildBlocks(text, dark);

        int keep = 0;
        while (keep < _elements.Count && keep < blocks.Count && _elements[keep].Key == blocks[keep].Key)
            keep++;

        while (_elements.Count > keep)
        {
            var last = _elements[^1];
            blocksHost.Children.Remove(last.Element);
            _elements.RemoveAt(_elements.Count - 1);
        }
        for (int i = keep; i < blocks.Count; i++)
        {
            var el = blocks[i].Factory();
            _elements.Add((blocks[i].Key, el));
            blocksHost.Children.Add(el);
        }
    }

    private IReadOnlyList<MdBlock> BuildBlocks(string text, bool dark)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<MdBlock>();
        var doc = Markdown.Parse(text, Pipeline);
        var blocks = doc.Cast<MarkdigBlock>().ToList();
        var result = new List<MdBlock>(blocks.Count);
        string themeKey = ThemeKey(dark);
        int i = 0;
        while (i < blocks.Count)
        {
            var block = blocks[i];
            if (block is HeadingBlock or ParagraphBlock)
            {
                // Contiguous flow blocks are merged into a single TextBlock (joined with
                // LineBreak) so text can be selected across lines/paragraphs at once.
                int start = i;
                int startLine = block.Line;
                int endLine = startLine;
                while (i < blocks.Count && blocks[i] is HeadingBlock or ParagraphBlock)
                {
                    endLine = blocks[i].Line;
                    i++;
                }
                var flow = blocks.GetRange(start, i - start);
                int startPos = LineStart(text, startLine);
                int endPos = i < blocks.Count ? LineStart(text, blocks[i].Line) : text.Length;
                int len = Math.Max(0, endPos - startPos);
                result.Add(new MdBlock($"{themeKey}flow:{FnvSpan(text, startPos, len)}", () => FlowTextBlock(flow)));
            }
            else
            {
                var nextLine = i + 1 < blocks.Count ? blocks[i + 1].Line : -1;
                var factory = BuildFactory(block, text, nextLine);
                if (factory is not null)
                {
                    result.Add(new MdBlock($"{themeKey}{BlockKind(block)}:{FnvSpan(text, BlockRawStart(text, block), BlockRawLength(text, block, nextLine))}", factory));
                }
                i++;
            }
        }
        return result;
    }

    private static string ThemeKey(bool dark) => dark ? "d:" : "l:";

    private Func<UIElement>? BuildFactory(MarkdigBlock block, string text, int nextLine) => block switch
    {
        CodeBlock c => () => RenderCode(c),
        ListBlock l => () => RenderList(l),
        QuoteBlock q => () => RenderQuote(q),
        ThematicBreakBlock => RenderThematicBreak,
        Table t => () => RenderTable(t, text, nextLine),
        HtmlBlock hb => () => RenderRawSourceText(hb.Lines.ToString()),
        _ => null,
    };

    private static string BlockKind(MarkdigBlock block) => block switch
    {
        HeadingBlock h => $"h{h.Level}",
        ParagraphBlock => "p",
        CodeBlock => "code",
        ListBlock => "list",
        QuoteBlock => "quote",
        ThematicBreakBlock => "hr",
        Table => "table",
        HtmlBlock => "html",
        _ => "block",
    };

    // ── block renderers ──────────────────────────────────────────────

    private UIElement FlowTextBlock(IReadOnlyList<MarkdigBlock> flowBlocks)
    {
        var tb = new TextBlock
        {
            FontSize = 13.5,
            Foreground = _theme.PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(0, 0, 0, 2),
        };
        for (int i = 0; i < flowBlocks.Count; i++)
        {
            switch (flowBlocks[i])
            {
                case HeadingBlock h:
                    double size = h.Level switch { 1 => 22, 2 => 19, 3 => 17, 4 => 15, 5 => 14, _ => 13.5 };
                    BuildInlines(h.Inline, tb.Inlines, new InlineStyle(FontSize: size, SemiBold: true));
                    break;
                case ParagraphBlock p:
                    BuildInlines(p.Inline, tb.Inlines, default);
                    break;
            }
            if (i < flowBlocks.Count - 1) tb.Inlines.Add(new LineBreak());
        }
        return tb;
    }

    private UIElement RenderCode(CodeBlock code)
    {
        var text = code.Lines.ToString();
        // No explicit Foreground: unscoped tokens (plain identifiers) emit Foreground=null runs
        // that inherit this TextBlock's brush — baking one here froze them to the build-time
        // theme (black-on-dark after a flip). Left unset, Uno's theme walk keeps it current.
        var tb = new TextBlock
        {
            FontFamily = CodeFonts.Current,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        // Fenced code carries its language in Info (e.g. ```ts / ```csharp); indented code has
        // none. Colorize via ColorCode when we can resolve the language, else plain text.
        var info = code is FencedCodeBlock fenced ? fenced.Info : null;
        if (!CodeHighlighter.Colorize(tb, text, info))
            tb.Text = text;

        return new Border
        {
            Background = _theme.SystemNeutralBackground,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 2, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = tb,
        };
    }

    private UIElement RenderList(ListBlock list)
    {
        int index = int.TryParse(list.OrderedStart, out var start) ? start : 1;
        var stack = new StackPanel { Spacing = 2, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;
            var marker = list.IsOrdered ? $"{index}. " : "•  ";
            index++;

            var content = new StackPanel { Spacing = 4 };
            foreach (var child in li)
            {
                content.Children.Add(child switch
                {
                    ParagraphBlock pb => BuildListItemParagraph(pb, marker),
                    CodeBlock c => RenderCode(c),
                    QuoteBlock q => RenderQuote(q),
                    ListBlock nested => RenderList(nested),
                    ThematicBreakBlock => RenderThematicBreak(),
                    _ => null,
                });
            }
            stack.Children.Add(content);
        }
        return stack;
    }

    private UIElement BuildListItemParagraph(ParagraphBlock p, string marker)
    {
        var tb = new TextBlock
        {
            FontSize = 13.5,
            Foreground = _theme.PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Margin = new Thickness(20, 0, 0, 0),
            Inlines = { new Run { Text = marker, Foreground = _theme.SecondaryText } },
        };
        BuildInlines(p.Inline, tb.Inlines, default);
        return tb;
    }

    private UIElement RenderQuote(QuoteBlock quote)
    {
        var inner = new StackPanel { Spacing = 6, Margin = new Thickness(10, 0, 0, 0) };
        var flow = new List<MarkdigBlock>();
        void Flush()
        {
            if (flow.Count > 0)
            {
                inner.Children.Add(FlowTextBlock(flow));
                flow.Clear();
            }
        }
        foreach (var child in quote)
        {
            switch (child)
            {
                case HeadingBlock or ParagraphBlock:
                    flow.Add(child);
                    break;
                default:
                    Flush();
                    inner.Children.Add(child switch
                    {
                        QuoteBlock nested => RenderQuote(nested),
                        ListBlock l => RenderList(l),
                        CodeBlock c => RenderCode(c),
                        ThematicBreakBlock => RenderThematicBreak(),
                        _ => null,
                    });
                    break;
            }
        }
        Flush();
        return new Border
        {
            Background = _theme.SubtleFill,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 2, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = inner,
        };
    }

    private UIElement RenderThematicBreak() => new Border
    {
        Height = 1,
        Background = _theme.DividerStroke,
        Margin = new Thickness(0, 6, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private UIElement RenderTable(Table table, string text, int nextLine)
    {
        int columnCount = table.ColumnDefinitions.Count;
        if (columnCount == 0 || table.Count == 0 || !table.IsValid())
            return RenderRawSource(text, table, nextLine);

        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        for (int c = 0; c < columnCount; c++)
        {
            double width = table.ColumnDefinitions[c].Width;
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width > 0 ? width : 1, GridUnitType.Star) });
        }

        var occupied = new List<HashSet<int>>();
        for (int r = 0; r < table.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            occupied.Add(new HashSet<int>());
            if (table[r] is not TableRow row) continue;
            for (int ci = 0; ci < row.Count; ci++)
            {
                if (row[ci] is not TableCell cell) continue;
                int col = cell.ColumnIndex >= 0 ? cell.ColumnIndex : NextFreeColumn(occupied[r], 0);
                int span = Math.Max(1, cell.ColumnSpan);
                for (int cc = col; cc < col + span && cc < columnCount; cc++)
                    occupied[r].Add(cc);

                var tb = new TextBlock
                {
                    FontSize = 12.5,
                    Foreground = _theme.PrimaryText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal,
                };
                bool firstBlock = true;
                foreach (var block in cell)
                {
                    if (!firstBlock) tb.Inlines.Add(new LineBreak());
                    firstBlock = false;
                    switch (block)
                    {
                        case ParagraphBlock p:
                            BuildInlines(p.Inline, tb.Inlines, default);
                            break;
                        case CodeBlock code:
                            tb.Inlines.Add(new Run { Text = code.Lines.ToString(), FontFamily = CodeFonts.Current });
                            break;
                    }
                }

                var align = col < columnCount ? table.ColumnDefinitions[col].Alignment : null;
                tb.HorizontalAlignment = align switch
                {
                    TableColumnAlign.Center => HorizontalAlignment.Center,
                    TableColumnAlign.Right => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                };

                var cellBorder = new Border
                {
                    Background = row.IsHeader ? _theme.SubtleFill : null,
                    Padding = new Thickness(10, 6, 10, 6),
                    BorderBrush = _theme.DividerStroke,
                    BorderThickness = new Thickness(
                        left: 0,
                        top: 0,
                        right: col + span >= columnCount ? 0 : 1,
                        bottom: r + Math.Max(1, cell.RowSpan) >= table.Count ? 0 : 1),
                    Child = tb,
                };
                Grid.SetRow(cellBorder, r);
                Grid.SetColumn(cellBorder, col);
                if (span > 1) Grid.SetColumnSpan(cellBorder, Math.Min(span, columnCount - col));
                if (cell.RowSpan > 1) Grid.SetRowSpan(cellBorder, cell.RowSpan);
                grid.Children.Add(cellBorder);
            }
        }

        return new Border
        {
            Background = _theme.CardBackground,
            BorderBrush = _theme.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 2, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = grid,
        };
    }

    private static int NextFreeColumn(HashSet<int> occupied, int start)
    {
        while (occupied.Contains(start)) start++;
        return start;
    }

    private UIElement RenderRawSource(string text, MarkdigBlock block, int nextLine)
    {
        int start = BlockRawStart(text, block);
        int length = BlockRawLength(text, block, nextLine);
        return RenderRawSourceText(length > 0 ? text.Substring(start, length) : "");
    }

    private UIElement RenderRawSourceText(string raw) => new Border
    {
        Background = _theme.SystemNeutralBackground,
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(0, 2, 0, 4),
        HorizontalAlignment = HorizontalAlignment.Stretch,
        Child = new TextBlock
        {
            Text = raw,
            FontFamily = CodeFonts.Current,
            FontSize = 12,
            Foreground = _theme.PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        },
    };

    private UIElement PlainTextBlock(string text) => new TextBlock
    {
        Text = text,
        FontSize = 13.5,
        Foreground = _theme.PrimaryText,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    // ── inline rendering ─────────────────────────────────────────────

    private readonly record struct InlineStyle(bool Bold = false, bool Italic = false, bool Code = false, bool Underline = false, double FontSize = 0, bool SemiBold = false);

    private void BuildInlines(ContainerInline? container, InlineCollection target, InlineStyle style)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            BuildInline(inline, target, style);
        }
    }

    private void BuildInline(MarkdigInline inline, InlineCollection target, InlineStyle style)
    {
        switch (inline)
        {
            case LiteralInline lit:
                AddRun(target, lit.Content.ToString(), style);
                break;

            case CodeInline code:
                AddRun(target, code.Content.ToString(), style with { Code = true });
                break;

            case EmphasisInline em when em.DelimiterCount >= 2:
                BuildInlines(em, target, style with { Bold = true });
                break;

            case EmphasisInline em:
                BuildInlines(em, target, style with { Italic = true });
                break;

            case LinkInline link when !link.IsImage:
                if (link.Url is { Length: > 0 } && Uri.TryCreate(link.Url, UriKind.Absolute, out var uri))
                {
                    var hyper = new Hyperlink { NavigateUri = uri };
                    BuildInlines(link, hyper.Inlines, style);
                    target.Add(hyper);
                }
                else
                {
                    BuildInlines(link, target, style);
                }
                break;

            case AutolinkInline auto:
                // Email autolinks (<foo@bar.com>) carry the bare address; prefix mailto: so the
                // Hyperlink navigates correctly while the displayed text stays the address.
                var autoUrl = auto.IsEmail && !auto.Url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                    ? "mailto:" + auto.Url
                    : auto.Url;
                if (Uri.TryCreate(autoUrl, UriKind.Absolute, out var autoUri))
                {
                    target.Add(new Hyperlink { NavigateUri = autoUri, Inlines = { new Run { Text = auto.Url } } });
                }
                else
                {
                    AddRun(target, auto.Url, style);
                }
                break;

            case LineBreakInline:
                target.Add(new LineBreak());
                break;

            case HtmlInline html:
                AddRun(target, html.Tag.ToString(), style);
                break;

            case HtmlEntityInline entity:
                AddRun(target, entity.Original.ToString(), style);
                break;

            default:
                break;
        }
    }

    private void AddRun(InlineCollection target, string text, InlineStyle style)
    {
        if (text.Length == 0) return;
        var run = new Run { Text = text };
        if (style.Bold || style.SemiBold) run.FontWeight = FontWeights.SemiBold;
        if (style.Italic) run.FontStyle = FontStyle.Italic;
        if (style.Underline) run.TextDecorations = TextDecorations.Underline;
        if (style.FontSize > 0) run.FontSize = style.FontSize;
        if (style.Code)
        {
            run.FontFamily = CodeFonts.Current;
            // Secondary accent (hue-shifted from the primary) so snippets read distinct from
            // accent-colored links; falls back to the attention color when no solid accent exists.
            run.Foreground = AccentPalette.InlineCodeBrush(_theme) ?? _theme.SystemAttention;
        }
        target.Add(run);
    }

    // ── helpers ──────────────────────────────────────────────────────

    private static int BlockRawStart(string text, MarkdigBlock block) => LineStart(text, block.Line);

    private static int BlockRawLength(string text, MarkdigBlock block, int nextLine)
    {
        int start = LineStart(text, block.Line);
        int end = nextLine < 0 ? text.Length : LineStart(text, nextLine);
        return Math.Max(0, end - start);
    }

    private static int LineStart(string text, int line)
    {
        if (line <= 0) return 0;
        int idx = 0;
        for (int i = 0; i < line; i++)
        {
            int n = text.IndexOf('\n', idx);
            if (n < 0) return text.Length;
            idx = n + 1;
        }
        return Math.Min(idx, text.Length);
    }

    private static uint Fnv(string s)
    {
        uint hash = 2166136261;
        foreach (var c in s) { hash ^= c; hash *= 16777619; }
        return hash;
    }

    private static uint FnvSpan(string s, int start, int length)
    {
        uint hash = 2166136261;
        int end = start + length;
        for (int i = start; i < end; i++) { hash ^= s[i]; hash *= 16777619; }
        return hash;
    }
}
