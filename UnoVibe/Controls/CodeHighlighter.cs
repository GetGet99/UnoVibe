using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Documents;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Style = ColorCode.Styling.Style;

namespace UnoVibe.Controls;

/// <summary>
/// ColorCode-based syntax highlighting for a plain <see cref="TextBlock"/>.
///
/// Uno has no RichTextBlock, so this mirrors ColorCode's WinUI <c>RichTextBlockFormatter</c>
/// but emits styled <see cref="Run"/>s into a TextBlock's <see cref="TextBlock.Inlines"/>
/// (the same approach <see cref="MarkdownView"/> uses for inline markdown). Parsing stays in
/// ColorCode.Core (regex-combination per language, cached); only the output target (Inlines
/// instead of RichTextBlock.Blocks) and the niche (markdown fenced-code blocks) are new.
///
/// Each call creates a fresh formatter: ColorCode's language regexes are compiled once and
/// cached internally, so warm parses run in ~0.1 ms — fine for MarkdownView's per-delta
/// re-render. The style dictionary is picked from the app theme (dark/light), mirroring the
/// heuristic <see cref="AccentPalette"/> already uses.
/// </summary>
public static class CodeHighlighter
{
    private static readonly UISettings Ui = new();

    // StyleDictionary.DefaultDark/DefaultLight build a fresh dictionary on every access, so
    // cache one instance each instead of re-allocating ~50 Style objects on every highlight.
    private static readonly StyleDictionary DarkStyles = StyleDictionary.DefaultDark;
    private static readonly StyleDictionary LightStyles = StyleDictionary.DefaultLight;

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    /// <summary>
    /// Resolves a fenced-code info string (e.g. "ts", "csharp", "json") to a ColorCode
    /// language via id + alias matching. Returns null when the info is empty or unknown —
    /// the caller then falls back to plain text.
    /// </summary>
    public static ILanguage? ResolveLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        var trimmed = info.Trim();
        return trimmed.Length > 0 ? Languages.FindById(trimmed) : null;
    }

    /// <summary>
    /// Colorizes <paramref name="source"/> and appends styled runs to <paramref name="target"/>'s
    /// Inlines. Returns true when a language was resolved; false when <paramref name="language"/>
    /// is null/unknown or the source is empty (caller keeps / builds plain text instead).
    /// </summary>
    public static bool Colorize(TextBlock target, string source, string? language)
    {
        var lang = ResolveLanguage(language);
        if (lang is null || source.Length == 0) return false;

        var styles = IsDarkTheme() ? DarkStyles : LightStyles;
        var formatter = new TextBlockFormatter(styles);
        formatter.FormatInlines(source, lang, target.Inlines);
        return true;
    }

    /// <summary>Same dark/light detection as AccentPalette: the WinUI system background color.</summary>
    private static bool IsDarkTheme() => Ui.GetColorValue(UIColorType.Background).R < 255 / 2;

    private static SolidColorBrush BrushFromHex(string hex)
    {
        if (BrushCache.TryGetValue(hex, out var cached)) return cached;

        var h = hex.TrimStart('#');
        Color color;
        if (h.Length >= 8 && byte.TryParse(h.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var a))
            color = Color.FromArgb(a, ParseHex(h, 2), ParseHex(h, 4), ParseHex(h, 6));
        else if (h.Length >= 6)
            color = Color.FromArgb(255, ParseHex(h, 0), ParseHex(h, 2), ParseHex(h, 4));
        else
            color = Color.FromArgb(0, 0, 0, 0);

        var brush = new SolidColorBrush(color);
        BrushCache[hex] = brush;
        return brush;
    }

    private static byte ParseHex(string h, int offset) =>
        byte.TryParse(h.AsSpan(offset, 2), System.Globalization.NumberStyles.HexNumber, null, out var b) ? b : (byte)0;

    /// <summary>Colorized single scope fragment: text + the resolved style for the scope name.</summary>
    private readonly record struct StyledRun(string Text, Style? Style);

    /// <summary>
    /// A <see cref="CodeColorizerBase"/> that writes parsed fragments as styled <see cref="Run"/>s
    /// into a TextBlock's InlineCollection. Nested scopes (e.g. escape sequences inside a string)
    /// are tracked with a stack so the innermost scope that defines a style wins — the original
    /// WinUI formatter uses a single "previous scope" and drops a parent's color after a child ends.
    /// </summary>
    private sealed class TextBlockFormatter : CodeColorizerBase
    {
        private InlineCollection? _inlines;

        public TextBlockFormatter(StyleDictionary styles) : base(styles, null)
        {
        }

        public void FormatInlines(string sourceCode, ILanguage language, InlineCollection inlines)
        {
            _inlines = inlines;
            languageParser.Parse(sourceCode, language, (parsed, scopes) => Write(parsed, scopes));
        }

        private InlineCollection Inlines => _inlines!;

        protected override void Write(string parsedSourceCode, IList<Scope> scopes)
        {
            if (scopes.Count == 0)
            {
                if (parsedSourceCode.Length > 0) Emit(new StyledRun(parsedSourceCode, null));
                return;
            }

            // Flatten the scope tree into (index, isStart, scope) markers, then walk the chunk
            // applying the innermost styled scope on the stack to each text run.
            var events = new List<(int Index, bool IsStart, Scope Scope)>();
            foreach (var scope in scopes) Flatten(scope, events);
            events.SortStable((a, b) => a.Index.CompareTo(b.Index));

            var stack = new List<Scope>();
            int offset = 0;
            foreach (var (index, isStart, scope) in events)
            {
                int clamped = Math.Clamp(index, offset, parsedSourceCode.Length);
                if (clamped > offset)
                    Emit(new StyledRun(parsedSourceCode.Substring(offset, clamped - offset), EffectiveStyle(stack)));
                if (isStart) stack.Add(scope);
                else if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                offset = clamped;
            }
            if (offset < parsedSourceCode.Length)
                Emit(new StyledRun(parsedSourceCode.Substring(offset), EffectiveStyle(stack)));
        }

        private static void Flatten(Scope scope, List<(int Index, bool IsStart, Scope Scope)> events)
        {
            events.Add((scope.Index, true, scope));
            foreach (var child in scope.Children) Flatten(child, events);
            events.Add((scope.Index + scope.Length, false, scope));
        }

        /// <summary>The innermost active scope that has a style entry (ColorCode's "previous scope").</summary>
        private Style? EffectiveStyle(List<Scope> stack)
        {
            for (int i = stack.Count - 1; i >= 0; i--)
            {
                var name = stack[i].Name;
                if (Styles.Contains(name)) return Styles[name];
            }
            return null;
        }

        private void Emit(StyledRun run)
        {
            if (run.Text.Length == 0) return;
            var element = new Run { Text = run.Text };
            if (run.Style is { } style)
            {
                if (!string.IsNullOrWhiteSpace(style.Foreground))
                    element.Foreground = BrushFromHex(style.Foreground);
                if (style.Bold) element.FontWeight = FontWeights.Bold;
                if (style.Italic) element.FontStyle = FontStyle.Italic;
            }
            Inlines.Add(element);
        }
    }
}