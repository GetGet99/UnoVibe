using Microsoft.UI.Xaml.Documents;
using UnoVibe.Controls.ToolViews;

namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Documents;
    using Microsoft.UI.Text;
    string? Text;
    string? FilePath;
    bool ShowAll = false;
    <root>
        host = <StackPanel Spacing=2 />
    </root>
    """)]
partial class CodeView : IQuickMarkupComponent<UIElement>
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
            FontFamily = CodeFontsHelper.Current,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        FillInlines(text, visible, lineCount, overflow);
        text.ActualThemeChanged += (_, _) => FillInlines(text, visible, lineCount, overflow);
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
        var lineCount = preview.Split('\n').Length - (preview.Length > 0 && preview[^1] == '\n' ? 1 : 0);
        return (preview, overflow, lineCount);
    }

    private void FillInlines(TextBlock text, string visible, int lineCount, bool overflow)
    {
        text.Inlines.Clear();
        BuildBlock(text, visible, lineCount, overflow);
    }

    private void BuildBlock(TextBlock text, string source, int previewLineCount, bool overflow)
    {
        var inlines = text.Inlines;
        var lang = CodeHighlighter.ResolveLanguageFromPath(FilePath);
        var runs = lang is not null
            ? CodeHighlighter.ColorizeRuns(source, lang.Id, text)
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
        var fallback = CodeHighlighter.PlainTextBrush(text);
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
                    inlines.Add(CodeHighlighter.ToRun(part, run.Style, fallback));
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