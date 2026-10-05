using System.Text.Json;

namespace UnoVibe.Stores;

public enum SendPromptMode
{
    OnNextToolCall,

    Queue,

    SendImmediately,
}

static class SettingKinds
{
    public const string Text = "text";
    public const string Choice = "choice";
    public const string Toggle = "toggle";
}

public sealed record SettingSpec(
    string Key,
    string Label,
    string Description,
    string Kind,
    SettingOption[]? Options = null,
    string? Placeholder = null);

static class SettingsStore
{
    public const string EditorCommandKey = "editor.command";
    public const string SendModeKey = "send.mode";
    public const string CodeFontKey = "text.codefont";
    public const string ExpandSkillsKey = "command.skills";
    public const string AutoContinueKey = "turn.autocontinue";

    private static readonly string Dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly object Gate = new();
    private static bool _loaded;
    private static string _lastWritten = "";
    private static FileSystemWatcher? _watcher;
    private static int _reloadScheduled;

    public static event Action? Changed;

    public static string EditorCommand { get; set; } = "code";

    public static SendPromptMode SendMode { get; set; } = SendPromptMode.OnNextToolCall;

    public static string CodeFont { get; set; } = CodeFontsHelper.DefaultValue;

    public static bool ExpandSkills { get; set; } = true;

    public static bool AutoContinueOnThinking { get; set; } = false;

    public static IReadOnlyList<SettingSpec> Specs => _specs ??= BuildSpecs();

    private static IReadOnlyList<SettingSpec>? _specs;

    private static IReadOnlyList<SettingSpec> BuildSpecs()
    {
        var codeFontOptions = new List<SettingOption>
        {
            new(CodeFontsHelper.DefaultValue, "Default (per platform)"),
        };
        foreach (var name in SystemFontsHelper.Families)
            codeFontOptions.Add(new SettingOption(name, name));

        return new SettingSpec[]
        {
            new(
                EditorCommandKey,
                "Default IDE/Editor",
                "Command used to open a folder in your editor, e.g. `code path/to/folder`. Set to any editor CLI on your PATH (`code`, `cursor`, `windsurf`, `zed`, ...).",
                SettingKinds.Text,
                Placeholder: "code"),
            new(
                SendModeKey,
                "Send message default",
                "What sending a message does while a turn is already running. \"On next tool call\" sends immediately and lets the server order it; \"Queue\" holds it until the session is idle; \"Send immediately\" interrupts the running turn and sends right away. This is the split send-button's primary action while busy; its dropdown offers one-time overrides.",
                SettingKinds.Choice,
                new SettingOption[]
                {
                    new("OnNextToolCall", "On next tool call"),
                    new("Queue", "Queue until idle"),
                    new("SendImmediately", "Send immediately"),
                }),
            new(
                CodeFontKey,
                "Code font",
                "Monospaced font used for code blocks, tool output, and diffs. \"Default (per platform)\" picks a font that ships with the OS — Consolas on Windows, DejaVu Sans Mono on Linux, Menlo on macOS. The list below contains every font installed on this device.",
                SettingKinds.Choice,
                codeFontOptions.ToArray()),
            new(
                ExpandSkillsKey,
                "Expand skills via slash commands",
                "When on, typing /skill-name invokes that skill like a command (default, TUI behavior). When off, skill-only names are sent as a plain message instead; real commands and MCP prompts still expand, and a name matching both a command and a skill always runs the command.",
                SettingKinds.Toggle),
            new(
                AutoContinueKey,
                "Automatically continue (Thinking stop)",
                "When a turn stops with the chat ending on an unfinished Thinking block, automatically send a message to resume agent. Max 50 auto-continues.",
                SettingKinds.Toggle),
        };
    }

    public static string GetValue(string key) => key switch
    {
        EditorCommandKey => EditorCommand,
        SendModeKey => SendMode.ToString(),
        CodeFontKey => CodeFont,
        ExpandSkillsKey => ExpandSkills ? "true" : "false",
        AutoContinueKey => AutoContinueOnThinking ? "true" : "false",
        _ => "",
    };

    public static void SetValue(string key, string value)
    {
        switch (key)
        {
            case EditorCommandKey:
                EditorCommand = value;
                break;
            case SendModeKey:
                if (Enum.TryParse<SendPromptMode>(value, out var mode)) SendMode = mode;
                else return;
                break;
            case CodeFontKey:
                CodeFont = value;
                break;
            case ExpandSkillsKey:
                if (bool.TryParse(value, out var expand)) ExpandSkills = expand;
                else return;
                break;
            case AutoContinueKey:
                if (bool.TryParse(value, out var autoContinue)) AutoContinueOnThinking = autoContinue;
                else return;
                break;
            default:
                return;
        }
        Save();
        Changed?.Invoke();
    }

    public static void Load()
    {
        lock (Gate)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    Apply(json);
                    _lastWritten = json;
                }
            }
            catch
            {
            }
            StartWatcher();
        }
    }

    private static void Apply(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize(json, AppJsonContext.Default.SettingsFileModel);
            if (file is null) return;
            if (file.EditorCommand is not null) EditorCommand = file.EditorCommand;
            if (Enum.TryParse<SendPromptMode>(file.SendMode, out var mode)) SendMode = mode;
            if (file.CodeFont is not null) CodeFont = file.CodeFont;
            if (file.ExpandSkills is not null) ExpandSkills = file.ExpandSkills.Value;
            if (file.AutoContinueOnThinking is not null) AutoContinueOnThinking = file.AutoContinueOnThinking.Value;
        }
        catch (JsonException)
        {
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = new SettingsFileModel { EditorCommand = EditorCommand, SendMode = SendMode.ToString(), CodeFont = CodeFont, ExpandSkills = ExpandSkills, AutoContinueOnThinking = AutoContinueOnThinking };
            var json = JsonSerializer.Serialize(file, AppJsonContext.Default.SettingsFileModel);
            lock (Gate)
            {
                _lastWritten = json;
                File.WriteAllText(FilePath, json);
            }
        }
        catch
        {
        }
    }

    private static void StartWatcher()
    {
        try
        {
            _watcher = new FileSystemWatcher(Dir, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) => OnFileChanged();
            _watcher.Created += (_, _) => OnFileChanged();
            _watcher.Renamed += (_, _) => OnFileChanged();
        }
        catch
        {
            _watcher = null;
        }
    }

    private static void OnFileChanged()
    {
        if (Interlocked.CompareExchange(ref _reloadScheduled, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300);
                var applied = false;
                lock (Gate)
                {
                    try
                    {
                        if (!File.Exists(FilePath)) return;
                        var json = File.ReadAllText(FilePath);
                        if (json == _lastWritten) return;
                        Apply(json);
                        _lastWritten = json;
                        applied = true;
                    }
                    catch
                    {
                    }
                }
                if (applied) Changed?.Invoke();
            }
            finally
            {
                Interlocked.Exchange(ref _reloadScheduled, 0);
            }
        });
    }

    internal sealed class SettingsFileModel
    {
        public string? EditorCommand { get; set; }
        public string? SendMode { get; set; }
        public string? CodeFont { get; set; }
        public bool? ExpandSkills { get; set; }
        public bool? AutoContinueOnThinking { get; set; }
    }
}
