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
/// re-render. The style dictionary is picked from the target element's resolved
/// <see cref="FrameworkElement.ActualTheme"/> (falling back to the system background-color
/// poll <see cref="AccentPalette"/> uses when no element is given), so callers can re-colorize
/// on <c>ActualThemeChanged</c> to keep already-rendered blocks readable across theme flips.
/// </summary>
public static class CodeHighlighter
{
    private static readonly UISettings Ui = new();

    // StyleDictionary.DefaultDark/DefaultLight build a fresh dictionary on every access, so
    // cache one instance each instead of re-allocating ~50 Style objects on every highlight.
    private static readonly StyleDictionary DarkStyles = StyleDictionary.DefaultDark;
    private static readonly StyleDictionary LightStyles = StyleDictionary.DefaultLight;

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    /// <summary>Resolves a fenced-code info string (e.g. "ts", "csharp", "json") to a ColorCode
    /// language via id + alias matching. Returns null when the info is empty or unknown —
    /// the caller then falls back to plain text.</summary>
    public static ILanguage? ResolveLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        var trimmed = info.Trim();
        return trimmed.Length > 0 ? Languages.FindById(trimmed) : null;
    }

    // Extension (lowercase, leading dot) -> ColorCode language id/alias for file paths.
    // Mirrors the TUI's util/filetype.ts LANGUAGE_EXTENSIONS for the subset ColorCode supports.
    private static readonly Dictionary<string, string> ExtensionLanguages = new()
    {
        [".c"] = "cpp",
        [".cc"] = "cpp",
        [".cpp"] = "cpp",
        [".cxx"] = "cpp",
        [".c++"] = "cpp",
        [".cs"] = "c#",
        [".csx"] = "c#",
        [".css"] = "css",
        [".fs"] = "fsharp",
        [".fsi"] = "fsharp",
        [".fsx"] = "fsharp",
        [".fsscript"] = "fsharp",
        [".hs"] = "haskell",
        [".lhs"] = "haskell",
        [".html"] = "html",
        [".htm"] = "html",
        [".java"] = "java",
        [".js"] = "javascript",
        [".mjs"] = "javascript",
        [".cjs"] = "javascript",
        [".jsx"] = "javascript",
        [".json"] = "json",
        [".md"] = "markdown",
        [".markdown"] = "markdown",
        [".php"] = "php",
        [".ps1"] = "powershell",
        [".psm1"] = "powershell",
        [".py"] = "python",
        [".sql"] = "sql",
        [".ts"] = "typescript",
        [".mts"] = "typescript",
        [".cts"] = "typescript",
        [".tsx"] = "typescript",
        [".xml"] = "xml",
        [".xaml"] = "xml",
        [".axml"] = "xml",
    };

    /// <summary>
    /// Resolves a file/directory path's extension to a ColorCode language (e.g. "src/App.cs" →
    /// "c#"), mirroring the TUI's <c>util/filetype.ts</c>. Returns null for unknown extensions,
    /// "\d+.png" image thumbnails, or paths with no extension — the caller falls back to plain.
    /// </summary>
    public static ILanguage? ResolveLanguageFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fileName = Path.GetFileName(path);
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1) return null;
        var ext = fileName.Substring(dot).ToLowerInvariant();
        return ExtensionLanguages.TryGetValue(ext, out var id) ? Languages.FindById(id) : null;
    }

    /// <summary>
    /// Colorizes <paramref name="source"/> and appends styled runs to <paramref name="target"/>'s
    /// Inlines. Returns true when a language was resolved; false when <paramref name="language"/>
    /// is null/unknown or the source is empty (caller keeps / builds plain text instead).
    /// </summary>
    public static bool Colorize(TextBlock target, string source, string? language)
    {
        var runs = ColorizeRuns(source, language, target);
        if (runs is null) return false;
        var fallback = PlainTextBrush(target);
        foreach (var run in runs)
        {
            if (run.Text.Length == 0) continue;
            var element = ToRun(run.Text, run.Style, fallback);
            target.Inlines.Add(element);
        }
        return true;
    }

    /// <summary>
    /// Colorizes <paramref name="source"/> into a flat list of styled text fragments (null when the
    /// language can't be resolved or the source is empty). Lets a caller build custom Inlines —
    /// e.g. <see cref="CodeView"/> interleaves per-line line numbers with the highlighted runs.
    /// The dark/light palette is chosen from <paramref name="themeSource"/>'s resolved theme when
    /// given (call again from its ActualThemeChanged to re-colorize), else the system theme.
    /// </summary>
    public static IReadOnlyList<StyledRun>? ColorizeRuns(string source, string? language, FrameworkElement? themeSource = null)
    {
        var lang = ResolveLanguage(language);
        if (lang is null || source.Length == 0) return null;

        var styles = IsDarkTheme(themeSource) ? DarkStyles : LightStyles;
        var formatter = new TextBlockFormatter(styles);
        return formatter.FormatRuns(source, lang);
    }

    /// <summary>
    /// The plain-text brush of the palette <paramref name="themeSource"/> currently resolves to.
    /// Used as the fallback for unstyled fragments: a Run with an unset Foreground defaults to
    /// hardcoded black in Uno (TextElement.ForegroundProperty), which is unreadable on dark.
    /// Re-resolve per render — callers rebuild on ActualThemeChanged, so it tracks the theme.
    /// </summary>
    public static Brush PlainTextBrush(FrameworkElement? themeSource)
    {
        bool isDark = IsDarkTheme(themeSource);
        var styles = isDark ? DarkStyles : LightStyles;
        if (styles.TryGetValue(ScopeName.PlainText, out var style) && !string.IsNullOrWhiteSpace(style.Foreground))
            return BrushFromHex(style.Foreground);
        return new SolidColorBrush(isDark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0));
    }

    /// <summary>
    /// Builds a <see cref="Run"/> from a styled fragment (version of the formatter's Emit).
    /// Fragments without a style/foreground get <paramref name="fallback"/> (see
    /// <see cref="PlainTextBrush"/>) instead of Uno's hardcoded-black unset-Run default.
    /// </summary>
    public static Run ToRun(string text, Style? style, Brush? fallback = null) => new()
    {
        Text = text,
        Foreground = StyleBrush(style) ?? fallback,
        FontWeight = style is { Bold: true } ? FontWeights.Bold : FontWeights.Normal,
        FontStyle = style is { Italic: true } ? FontStyle.Italic : FontStyle.Normal,
    };

    private static Brush? StyleBrush(Style? style)
    {
        if (style is null) return null;
        return !string.IsNullOrWhiteSpace(style.Foreground) ? BrushFromHex(style.Foreground) : null;
    }

    /// <summary>
    /// Dark/light detection for palette picking: the element's resolved ActualTheme when available
    /// (honors any app-level RequestedTheme override), else the WinUI system background color —
    /// the same heuristic <see cref="AccentPalette"/> uses.
    /// </summary>
    public static bool IsDarkTheme(FrameworkElement? themeSource)
    {
        var theme = themeSource?.ActualTheme;
        return theme is ElementTheme.Dark
            || (theme is not ElementTheme.Light && Ui.GetColorValue(UIColorType.Background).R < 255 / 2);
    }

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
    public readonly record struct StyledRun(string Text, Style? Style);

    /// <summary>
    /// A <see cref="CodeColorizerBase"/> that writes parsed fragments as styled <see cref="Run"/>s
    /// into a TextBlock's InlineCollection. Nested scopes (e.g. escape sequences inside a string)
    /// are tracked with a stack so the innermost scope that defines a style wins — the original
    /// WinUI formatter uses a single "previous scope" and drops a parent's color after a child ends.
    /// </summary>
    private sealed class TextBlockFormatter : CodeColorizerBase
    {
        private readonly List<StyledRun> _runs = new();

        public TextBlockFormatter(StyleDictionary styles) : base(styles, null)
        {
        }

        public IReadOnlyList<StyledRun> FormatRuns(string sourceCode, ILanguage language)
        {
            _runs.Clear();
            languageParser.Parse(sourceCode, language, (parsed, scopes) => Write(parsed, scopes));
            return _runs;
        }

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
            _runs.Add(run);
        }
    }
}