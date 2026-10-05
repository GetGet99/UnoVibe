using ColorCode;
using ColorCode.Common;
using ColorCode.Parsing;
using ColorCode.Styling;
using Microsoft.UI.Xaml.Documents;
using Windows.UI;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Style = ColorCode.Styling.Style;

namespace UnoVibe.Controls;

static class CodeHighlighter
{
    private static readonly UISettings Ui = new();

    private static readonly StyleDictionary DarkStyles = StyleDictionary.DefaultDark;
    private static readonly StyleDictionary LightStyles = StyleDictionary.DefaultLight;

    private static readonly Dictionary<string, SolidColorBrush> BrushCache = new();

    public static ILanguage? ResolveLanguage(string? info)
    {
        if (string.IsNullOrWhiteSpace(info)) return null;
        var trimmed = info.Trim();
        return trimmed.Length > 0 ? Languages.FindById(trimmed) : null;
    }

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

    public static ILanguage? ResolveLanguageFromPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var fileName = Path.GetFileName(path);
        var dot = fileName.LastIndexOf('.');
        if (dot <= 0 || dot == fileName.Length - 1) return null;
        var ext = fileName.Substring(dot).ToLowerInvariant();
        return ExtensionLanguages.TryGetValue(ext, out var id) ? Languages.FindById(id) : null;
    }

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

    public static IReadOnlyList<StyledRun>? ColorizeRuns(string source, string? language, FrameworkElement? themeSource = null)
    {
        var lang = ResolveLanguage(language);
        if (lang is null || source.Length == 0) return null;

        var styles = IsDarkTheme(themeSource) ? DarkStyles : LightStyles;
        var formatter = new TextBlockFormatter(styles);
        return formatter.FormatRuns(source, lang);
    }

    public static Brush PlainTextBrush(FrameworkElement? themeSource)
    {
        bool isDark = IsDarkTheme(themeSource);
        var styles = isDark ? DarkStyles : LightStyles;
        if (styles.TryGetValue(ScopeName.PlainText, out var style) && !string.IsNullOrWhiteSpace(style.Foreground))
            return BrushFromHex(style.Foreground);
        return new SolidColorBrush(isDark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 0, 0, 0));
    }

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

    public readonly record struct StyledRun(string Text, Style? Style);

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