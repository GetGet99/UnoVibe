using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using QuickMarkup.WinUI;
using UnoVibe.Controls.ToolViews;
using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>
/// Renders a code file's content (write tool result) as a line-numbered, syntax-highlighted
/// block. The source is colorized as a whole via <see cref="CodeHighlighter.ColorizeRuns"/>
/// (so multi-line strings/comments span lines correctly) and the resulting runs are split at
/// line boundaries to interleave a muted line-number run per line + LineBreak in one TextBlock
/// (no RichTextBlock on Uno — same single-block approach as MarkdownView, so text stays
/// selectable across lines).
///
/// The language comes from the file path via <see cref="CodeHighlighter.ResolveLanguageFromPath"/>
/// (mirrors the TUI's <c>filetype.ts</c>); files whose extension ColorCode doesn't know fall
/// back to plain text with line numbers.
///
/// Long content collapses to <see cref="CodeMaxLines"/> preview lines with a "Show more ▾"
/// toggle (mirrors ToolViewShell). Self-contained: <c>Text=</c> + <c>FilePath=</c> bind
/// reactively and it re-renders on every change.
/// </summary>
[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Documents;
    using Microsoft.UI.Text;
    string Text = "";
    string FilePath = "";
    bool ShowAll = false;
    <root>
        host = <StackPanel Spacing=2 />
    </root>
    """)]
public partial class CodeView : IQuickMarkupComponent<UIElement>
{
    public const int CodeMaxLines = 60;
    public const int CodeMaxChars = CodeMaxLines * 160;

    private readonly ThemeBrushes _theme = ThemeBrushes.Global;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        TextProp.Watch(_ => Render(), immediete: true);
        ShowAllProp.Watch(_ => Render());
    }

    private void Render()
    {
        var content = Text ?? "";
        if (content.Length == 0)
        {
            host.Children.Clear();
            return;
        }

        var (visible, overflow, lineCount) = ShowAll
            ? (content, false, 0)
            : Collapse(content);

        host.Children.Clear();

        var box = new Border
        {
            Background = _theme.SystemNeutralBackground,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var text = new TextBlock
        {
            FontFamily = CodeFonts.Current,
            FontSize = 12,
            Foreground = _theme.PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        BuildBlock(text.Inlines, visible, lineCount, overflow);
        box.Child = text;
        host.Children.Add(box);

        if (overflow)
        {
            var toggle = new Button
            {
                Background = _theme.LayerFill,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 2, 8, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            toggle.Content = ShowAll ? "Show less ▴" : "Show more ▾";
            toggle.Click += (_, _) => ShowAll = !ShowAll;
            host.Children.Add(toggle);
        }
    }

    private (string Preview, bool Overflow, int LineCount) Collapse(string content)
    {
        var (preview, overflow) = ToolViewShared.CollapsePreview(content, CodeMaxLines, CodeMaxChars);
        // The preview may be truncated inside a line; lineCount lets the gutter keep numbering.
        var lineCount = preview.Split('\n').Length - (preview.Length > 0 && preview[^1] == '\n' ? 1 : 0);
        return (preview, overflow, lineCount);
    }

    /// <summary>
    /// Colorizes the source whole-then-line-splits the runs, emitting a line-number run at each
    /// line start and a LineBreak between lines. Falls back to a single plain run when the file
    /// path's language can't be resolved. A trailing muted "…" marks a collapsed preview (never
    /// numbered).
    /// </summary>
    private void BuildBlock(InlineCollection inlines, string source, int previewLineCount, bool overflow)
    {
        var lang = CodeHighlighter.ResolveLanguageFromPath(FilePath);
        var runs = lang is not null
            ? CodeHighlighter.ColorizeRuns(source, lang.Id)
            : null;

        int totalLines = previewLineCount > 0 ? previewLineCount : source.Split('\n').Length;
        if (source.Length > 0 && source[^1] == '\n') totalLines = Math.Max(1, totalLines - 1);
        int numWidth = Math.Max(2, totalLines.ToString().Length);

        if (runs is null)
        {
            runs = new[] { new CodeHighlighter.StyledRun(source, null) };
        }

        int line = 1;
        bool atLineStart = true;
        foreach (var run in runs)
        {
            var parts = run.Text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (atLineStart && part.Length > 0)
                {
                    inlines.Add(new Run
                    {
                        Text = line.ToString().PadLeft(numWidth) + "  ",
                        Foreground = _theme.TertiaryText,
                    });
                    atLineStart = false;
                }
                if (part.Length > 0)
                    inlines.Add(CodeHighlighter.ToRun(part, run.Style));
                bool hadNewline = i < parts.Length - 1;
                if (hadNewline)
                {
                    inlines.Add(new LineBreak());
                    line++;
                    atLineStart = true;
                }
            }
        }
        if (overflow)
        {
            inlines.Add(new LineBreak());
            inlines.Add(new Run { Text = "…", Foreground = _theme.TertiaryText });
        }
    }
}