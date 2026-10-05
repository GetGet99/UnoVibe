using Microsoft.UI.Xaml.Documents;
using Windows.UI.Text;
using UnoVibe.Controls.ToolViews;

namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Documents;
    using Microsoft.UI.Text;
    using Windows.UI;
    string? Diff;
    bool ShowAll = false;
    <root>
        host = <StackPanel Spacing=2 />
    </root>
    """)]
partial class DiffView : IQuickMarkupComponent<UIElement>
{
    public const int DiffMaxLines = 60;
    public const int DiffMaxChars = DiffMaxLines * 160;

    private readonly ThemeBrushes _theme = ThemeBrushes.Global;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
        DiffProp.Watch(_ => Render(), immediete: true);
        ShowAllProp.Watch(_ => Render());
    }

    private void Render()
    {
        var diff = Diff ?? "";
        if (diff.Length == 0)
        {
            host.Children.Clear();
            return;
        }

        var (content, overflow) = ShowAll
            ? (diff, false)
            : ToolViewShared.CollapsePreview(diff, DiffMaxLines, DiffMaxChars);

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
            Foreground = _theme.PrimaryText,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        BuildLines(text.Inlines, content, overflow);
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

    private void BuildLines(InlineCollection inlines, string diff, bool overflow)
    {
        int oldLine = 0, newLine = 0;
        int lineCount = 0;
        foreach (var raw in diff.Split('\n'))
        {
            var line = raw;
            if (line.Length > 0 && line.StartsWith(HunkHeader, StringComparison.Ordinal))
            {
                ParseHunk(line, ref oldLine, ref newLine);
                AppendLine(inlines, "", line, lineCount, _theme.Accent, FontWeights.Bold);
            }
            else if (IsMetadata(line))
            {
                AppendLine(inlines, "", line, lineCount, _theme.TertiaryText, FontWeights.Normal);
            }
            else if (line.Length > 0 && line[0] == '+')
            {
                AppendLine(inlines, FormatGutter(0, newLine), line, lineCount, _theme.SystemSuccess, FontWeights.Normal);
                newLine++;
            }
            else if (line.Length > 0 && line[0] == '-')
            {
                AppendLine(inlines, FormatGutter(oldLine, 0), line, lineCount, _theme.SystemCritical, FontWeights.Normal);
                oldLine++;
            }
            else
            {
                AppendLine(inlines, FormatGutter(oldLine, newLine), raw, lineCount, _theme.PrimaryText, FontWeights.Normal);
                oldLine++;
            }
            lineCount++;
        }
        if (overflow)
        {
            inlines.Add(new LineBreak());
            inlines.Add(new Run { Text = "…", Foreground = _theme.TertiaryText });
        }
    }

    private const string HunkHeader = "@@ ";

    private static void ParseHunk(string line, ref int oldLine, ref int newLine)
    {
        var afterMarker = line.Substring(HunkHeader.Length);
        var parts = afterMarker.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int i = 0;
        foreach (var part in parts)
        {
            if (i == 0 && part.Length > 0 && part[0] == '-') oldLine = ParseRangeStart(part);
            else if (i == 1 && part.Length > 0 && part[0] == '+') newLine = ParseRangeStart(part);
            else break;
            i++;
        }
    }

    private static int ParseRangeStart(string part)
    {
        var value = part.Substring(1);
        var comma = value.IndexOf(',');
        if (comma >= 0) value = value.Substring(0, comma);
        return int.TryParse(value, out var n) ? n : 0;
    }

    private static string FormatGutter(int oldLine, int newLine) =>
        (oldLine > 0 ? oldLine.ToString().PadLeft(4) : "    ") + " " +
        (newLine > 0 ? newLine.ToString().PadLeft(4) : "    ");

    private static bool IsMetadata(string line)
    {
        if (line.Length < 3) return false;
        var l = line;
        if (l.StartsWith("---", StringComparison.Ordinal)) return true;
        if (l.StartsWith("+++", StringComparison.Ordinal)) return true;
        if (l.StartsWith("diff ", StringComparison.Ordinal)) return true;
        if (l.StartsWith("index ", StringComparison.Ordinal)) return true;
        if (l.StartsWith("new file", StringComparison.Ordinal)) return true;
        if (l.StartsWith("deleted file", StringComparison.Ordinal)) return true;
        if (l.StartsWith("similarity index", StringComparison.Ordinal)) return true;
        if (l.StartsWith("\\ No newline at end of file", StringComparison.Ordinal)) return true;
        return false;
    }

    private void AppendLine(InlineCollection inlines, string gutter, string content, int lineCount, Brush? color, FontWeight weight)
    {
        if (gutter.Length > 0)
        {
            inlines.Add(new Run { Text = gutter + " ", Foreground = _theme.TertiaryText, FontWeight = weight });
        }
        inlines.Add(new Run { Text = content, Foreground = color, FontWeight = weight });
        if (lineCount > 0) inlines.Add(new LineBreak());
    }
}