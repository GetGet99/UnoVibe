using System.IO;
using System.Text.Json;
using UnoVibe.Models;

namespace UnoVibe.Services;

/// <summary>How sending a message behaves while a turn is already running.</summary>
public enum SendPromptMode
{
    /// <summary>Send immediately; the server serializes prompts itself (runs the message at the
    /// next agent step when a turn is busy) — the current/default behavior.</summary>
    OnNextToolCall,

    /// <summary>Hold prompts in a client-side queue (<see cref="SessionStore"/>'s
    /// <c>EnqueuePrompt</c>/<c>DrainPendingPromptsAsync</c>) and flush them one at a time when the
    /// session goes idle.</summary>
    Queue,

    /// <summary>Interrupt the running turn (abort) first, then send the prompt — the new message
    /// becomes the active request instead of waiting for the next agent step. When idle it sends
    /// like <see cref="OnNextToolCall"/>.</summary>
    SendImmediately,
}

/// <summary>The UI kinds a setting can have (values of <see cref="SettingSpec.Kind"/>); the settings
/// page renders the matching control per row. Strings so markup compares them directly.</summary>
public static class SettingKinds
{
    public const string Text = "text";
    public const string Choice = "choice";
    public const string Toggle = "toggle";
}

/// <summary>Static metadata describing one settings row. The settings page renders every
/// <see cref="SettingsStore.Specs"/> entry automatically, so a new setting is just a new spec
/// plus a <c>GetValue</c>/<c>SetValue</c> case — the UI needs no changes.</summary>
public sealed record SettingSpec(
    string Key,
    string Label,
    string Description,
    string Kind,
    SettingOption[]? Options = null,
    string? Placeholder = null);

/// <summary>
/// App settings: a single static source of truth for every window (and, via a file watcher,
/// every process). Values are persisted to <c>settings.json</c> under the app's local-data
/// directory and loaded once at startup. Typed static properties are the canonical store
/// (read live by the app logic, e.g. <see cref="FolderLauncher"/> and <see cref="SessionStore"/>);
/// the <see cref="Specs"/> registry + <see cref="GetValue"/>/<see cref="SetValue"/> bridge them to
/// the data-driven settings page.
///
/// Multi-window: the store is static, so all windows share the same values immediately.
/// Multi-process: a <see cref="FileSystemWatcher"/> reloads the file when another process writes
/// it (debounced + loop-guarded), and <see cref="Changed"/> notifies open settings pages to re-read.
/// </summary>
public static class SettingsStore
{
    public const string EditorCommandKey = "editor.command";
    public const string SendModeKey = "send.mode";
    public const string CodeFontKey = "text.codefont";

    private static readonly string Dir = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly object Gate = new();
    private static bool _loaded;
    private static string _lastWritten = "";
    private static FileSystemWatcher? _watcher;
    private static int _reloadScheduled;

    /// <summary>Raised after a setting changes (same window, another window, or another process).</summary>
    public static event Action? Changed;

    // ── Typed settings (the canonical values; read live by the app logic) ──────────

    /// <summary>Command used to open a folder in the user's editor (default: VS Code's <c>code</c>).</summary>
    public static string EditorCommand { get; set; } = "code";

    /// <summary>How a send behaves while the session is busy.</summary>
    public static SendPromptMode SendMode { get; set; } = SendPromptMode.OnNextToolCall;

    /// <summary>Monospaced font used for code blocks, tool output, and diffs. Empty string (the
    /// default) picks a font that ships with the OS (see <see cref="CodeFonts"/>); any other
    /// value is a font family name used verbatim.</summary>
    public static string CodeFont { get; set; } = CodeFonts.DefaultValue;

    /// <summary>The settings-page rows. Built lazily (on first settings open) so the Code font options
    /// can enumerate the user's installed fonts via <see cref="SystemFonts"/>; cached thereafter.
    /// Adding a setting = add a spec here + a GetValue/SetValue case.</summary>
    public static IReadOnlyList<SettingSpec> Specs => _specs ??= BuildSpecs();

    private static IReadOnlyList<SettingSpec>? _specs;

    private static IReadOnlyList<SettingSpec> BuildSpecs()
    {
        var codeFontOptions = new List<SettingOption>
        {
            new(CodeFonts.DefaultValue, "Default (per platform)"),
        };
        foreach (var name in SystemFonts.Families)
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
        };
    }

    /// <summary>Reads the current value of a setting by key (the UI-facing string form).</summary>
    public static string GetValue(string key) => key switch
    {
        EditorCommandKey => EditorCommand,
        SendModeKey => SendMode.ToString(),
        CodeFontKey => CodeFont,
        _ => "",
    };

    /// <summary>Sets a setting by key (UI-facing string form), persists it, and notifies listeners.</summary>
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
            default:
                return;
        }
        Save();
        Changed?.Invoke();
    }

    /// <summary>Loads the persisted settings once and starts the cross-process file watcher.</summary>
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
                // Best effort: a corrupt/missing file just yields the defaults.
            }
            StartWatcher();
        }
    }

    /// <summary>Applies persisted JSON to the typed settings (unknown/invalid values keep defaults).</summary>
    private static void Apply(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize(json, AppJsonContext.Default.SettingsFileModel);
            if (file is null) return;
            if (file.EditorCommand is not null) EditorCommand = file.EditorCommand;
            if (Enum.TryParse<SendPromptMode>(file.SendMode, out var mode)) SendMode = mode;
            if (file.CodeFont is not null) CodeFont = file.CodeFont;
        }
        catch (JsonException)
        {
            // Best effort.
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var file = new SettingsFileModel { EditorCommand = EditorCommand, SendMode = SendMode.ToString(), CodeFont = CodeFont };
            var json = JsonSerializer.Serialize(file, AppJsonContext.Default.SettingsFileModel);
            lock (Gate)
            {
                _lastWritten = json;
                File.WriteAllText(FilePath, json);
            }
        }
        catch
        {
            // Best effort: settings persistence must never break the UI.
        }
    }

    /// <summary>
    /// Watches settings.json for writes from other processes so every running instance follows
    /// the latest values (multi-process support). Only reacts to changes that were not written by
    /// this process (<see cref="_lastWritten"/> guard prevents a self-trigger loop).
    /// </summary>
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
            _watcher = null; // watcher unavailable — single-process sync only
        }
    }

    private static void OnFileChanged()
    {
        // Debounce: multiple rapid writes (own saves, other processes) coalesce into one reload.
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
                        if (json == _lastWritten) return; // our own write
                        Apply(json);
                        _lastWritten = json;
                        applied = true;
                    }
                    catch
                    {
                        // Best effort.
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

    /// <summary>On-disk shape of settings.json.</summary>
    internal sealed class SettingsFileModel
    {
        public string? EditorCommand { get; set; }
        public string? SendMode { get; set; }
        public string? CodeFont { get; set; }
    }
}
