using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages.Chat;

/// <summary>
/// A ContentDialog for connecting a provider directly from the app — a mirror of the TUI's
/// <c>/connect</c> dialog (<c>packages/tui/src/component/dialog-provider.tsx</c>). Walks:
/// provider list → auth method → (prompt inputs + API key) or OAuth (browser + code / auto)
/// → credential stored via the server, then refreshes the model options.
///
/// The TUI stores *credentials only* (<c>PUT /auth/{providerID}</c> / the oauth callback) —
/// provider *definitions* still live in opencode.json, so a custom ("Other") provider id saves
/// its key the same way and a toast tells the user to configure it in the config to use it.
///
/// API: set <see cref="Store"/>, await <see cref="LoadAsync"/>, then set <c>MarkupNode.XamlRoot</c>
/// and <c>ShowAsync()</c> — the component's root <em>is</em> the <see cref="ContentDialog"/>.
/// Close it from <see cref="Completed"/>.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    using Microsoft.UI.Xaml.Controls;
    bool Loading = true;
    string? LoadError;
    int Page = 0;                                // 0=provider list, 1=auth methods, 2=key/prompt form, 3=oauth
    int MethodIndex = 0;
    string ProviderId = "";
    string ProviderName = "";
    string Title = "";
    string PromptCaption = "";
    string ApiKey = "";
    string CustomId = "";
    string Code = "";
    string OauthInstructions = "";
    bool OauthNeedsCode;
    bool IsCustom;
    bool ShowPrompts;
    bool ShowKeyField;
    string Status = "";
    bool StatusError;
    bool Working = false;
    string Query = "";
    `ObservableCollection<ProviderRow>` Providers = `new()`;
    `List<ProviderAuthMethod>` Methods = `new()`;
    `IEnumerable<ProviderRow>` FilteredProviders => `FilterProviders(Providers.Reactive, Query)`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <ContentDialog Width=480 CloseButtonText="Close" Title=`Title.Length > 0 ? Title : "Connect a provider"`>
            <Grid Padding=`new Thickness(8, 0, 8, 0)` RowDefinitions=<>
                <RowDefinition Height=Auto />
                <RowDefinition Height=Auto />
                <RowDefinition Height=Auto />
            </>>
                <Grid Grid.Row=0 ColumnSpacing=8 Margin=`new Thickness(0, 4, 0, 10)` ColumnDefinitions=<>
                    <ColumnDefinition Width=Auto />
                    <ColumnDefinition />
                </>>
                    <Button Padding=`new Thickness(6, 2, 6, 2)` CornerRadius=4 VerticalAlignment=Center
                            Visibility=`Page > 0 ? Visibility.Visible : Visibility.Collapsed`
                            ToolTipService.ToolTip="Back" @Click+=`GoBack()`>
                        <AppSymbolIcon Symbol=Back FontSize=12 />
                    </Button>
                    <TextBlock Grid.Column=1 Text=`PromptCaption` FontSize=13 Foreground=`theme.SecondaryText`
                               VerticalAlignment=Center TextWrapping=Wrap
                               Visibility=`PromptCaption.Length > 0 ? Visibility.Visible : Visibility.Collapsed` />
                </Grid>

                <Border Grid.Row=1 MinHeight=320>
                <StackPanel Spacing=8>
                if (`Page == 0`)
                {
                    <StackPanel Spacing=8>
                        if (`Loading`)
                        {
                            <StackPanel Spacing=10 HorizontalAlignment=Center VerticalAlignment=Center Height=300>
                                <ProgressRing Width=28 Height=28 IsActive=true />
                                <TextBlock Text="Loading providers…" FontSize=13 Foreground=`theme.SecondaryText` />
                            </StackPanel>
                        }
                        if (`!Loading && LoadError is not null`)
                        {
                            <StackPanel Spacing=10 HorizontalAlignment=Center VerticalAlignment=Center Height=300>
                                <TextBlock Text=`LoadError` FontSize=13 Foreground=`theme.SystemCritical` TextWrapping=Wrap MaxWidth=380 />
                                <Button Content="Retry" @Click+=`await LoadAsync()` />
                            </StackPanel>
                        }
                        if (`!Loading && LoadError is null`)
                        {
                            <StackPanel Spacing=8>
                                <Grid ColumnSpacing=4 ColumnDefinitions=<>
                                    <ColumnDefinition />
                                    <ColumnDefinition Width=Auto />
                                </>>
                                    <TextBox Text<=>`Query` PlaceholderText="Search providers" Height=32
                                             VerticalContentAlignment=Center Padding=`new Thickness(28, 4, 8, 4)` />
                                    <AppSymbolIcon Symbol=Find FontSize=12 Foreground=`theme.TertiaryText`
                                            HorizontalAlignment=Left VerticalAlignment=Center
                                            Margin=`new Thickness(10, 0, 0, 0)` IsHitTestVisible=false />
                                </Grid>
                                <ScrollViewer MinHeight=300 MaxHeight=380 VerticalScrollBarVisibility=Auto HorizontalContentAlignment=Stretch>
                                    <StackPanel HorizontalAlignment=Stretch Spacing=2>
                                        foreach (var p in `FilteredProviders`; `p.Id`)
                                        {
                                            <Button Height=44 HorizontalAlignment=Stretch CornerRadius=6 Background=`new SolidColorBrush(Colors.Transparent)`
                                                    BorderThickness=0 Padding=`new Thickness(10, 0, 10, 0)` HorizontalContentAlignment=Stretch
                                                    @Click+=`SelectProvider(p)`>
                                                <Grid ColumnSpacing=10 ColumnDefinitions=<>
                                                    <ColumnDefinition />
                                                    <ColumnDefinition Width=Auto />
                                                </>>
                                                    <StackPanel Spacing=1 VerticalAlignment=Center>
                                                        <TextBlock Text=`p.Name` FontSize=14 TextTrimming=CharacterEllipsis />
                                                        <TextBlock Text=`p.Id` FontSize=11 Foreground=`theme.TertiaryText` TextTrimming=CharacterEllipsis />
                                                    </StackPanel>
                                                    <TextBlock Grid.Column=1 Text=`p.IsConnected ? "Connected" : ""` FontSize=11
                                                               Foreground=`theme.SystemSuccess` VerticalAlignment=Center />
                                                </Grid>
                                            </Button>
                                        }
                                        <Button Height=44 HorizontalAlignment=Stretch CornerRadius=6 Padding=`new Thickness(10, 0, 10, 0)`
                                                HorizontalContentAlignment=Stretch @Click+=`BeginCustom()`>
                                            <Grid ColumnSpacing=10 ColumnDefinitions=<>
                                                <ColumnDefinition />
                                                <ColumnDefinition Width=Auto />
                                            </>>
                                                <StackPanel Spacing=1 VerticalAlignment=Center>
                                                    <TextBlock Text="Other / Custom provider" FontSize=14 />
                                                    <TextBlock Text="Store an API key under a custom provider id" FontSize=11 Foreground=`theme.TertiaryText` />
                                                </StackPanel>
                                                <AppSymbolIcon Symbol=Add FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                                            </Grid>
                                        </Button>
                                        if (`Query.Trim().Length > 0 && !FilteredProviders.Any()`)
                                            <Border Padding=`new Thickness(10, 16, 10, 16)` HorizontalAlignment=Stretch>
                                                <TextBlock Text=`$"No providers match \"{Query.Trim()}\""` FontSize=12
                                                           Foreground=`theme.SecondaryText` HorizontalAlignment=Center />
                                            </Border>
                                        if (`Query.Trim().Length == 0 && Providers.Reactive.Count == 0`)
                                            <TextBlock Text="No providers available" FontSize=12 Foreground=`theme.SecondaryText`
                                                       HorizontalAlignment=Center Margin=`new Thickness(0, 16, 0, 0)` />
                                    </StackPanel>
                                </ScrollViewer>
                            </StackPanel>
                        }
                    </StackPanel>
                }
                if (`Page == 1`)
                {
                    <StackPanel Spacing=8>
                        <TextBlock Text=`$"How do you want to connect {ProviderName}?"` FontSize=13
                                   Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                        foreach (index; var m in `Methods`)
                        {
                            <Button Height=44 HorizontalAlignment=Stretch CornerRadius=6 Padding=`new Thickness(12, 0, 12, 0)`
                                    HorizontalContentAlignment=Stretch @Click+=`BeginMethod(index)`>
                                <Grid ColumnSpacing=10 ColumnDefinitions=<>
                                    <ColumnDefinition />
                                    <ColumnDefinition Width=Auto />
                                </>>
                                    <TextBlock Text=`m.Label` FontSize=14 VerticalAlignment=Center TextTrimming=CharacterEllipsis />
                                    <Border Grid.Column=1 Background=`theme.CardBackground` BorderBrush=`theme.CardStroke`
                                            BorderThickness=`new Thickness(1)` CornerRadius=10 Padding=`new Thickness(8, 2, 8, 2)`
                                            VerticalAlignment=Center>
                                        <TextBlock Text=`m.Type == "oauth" ? "OAuth" : "API key"` FontSize=10 Foreground=`theme.SecondaryText` />
                                    </Border>
                                </Grid>
                            </Button>
                        }
                    </StackPanel>
                }
                formHost = <StackPanel Spacing=10 Visibility=`Page == 2 ? Visibility.Visible : Visibility.Collapsed`>
                    if (`PromptCaption.Length > 0`)
                        <TextBlock Text=`PromptCaption` FontSize=13 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                    formPromptHost = <StackPanel Spacing=8 Visibility=`ShowPrompts ? Visibility.Visible : Visibility.Collapsed` />
                    if (`IsCustom`)
                    {
                        <StackPanel Spacing=6>
                            <TextBlock Text="Provider id" FontSize=11 Foreground=`theme.SecondaryText` />
                            <TextBox Text<=>`CustomId` PlaceholderText="e.g. my-provider" Height=32 VerticalContentAlignment=Center />
                        </StackPanel>
                    }
                    if (`ShowKeyField`)
                    {
                        <StackPanel Spacing=6>
                            <TextBlock Text="API key" FontSize=11 Foreground=`theme.SecondaryText` />
                            <PasswordBox Password<=>`ApiKey` PlaceholderText="Paste your API key" />
                        </StackPanel>
                    }
                    <StackPanel Orientation=Horizontal Spacing=10 HorizontalAlignment=Right>
                        <Button Content="Cancel" @Click+=`GoBack()` />
                        if (`Working`)
                            <ProgressRing Width=16 Height=16 VerticalAlignment=Center />
                        <Button Content=`IsCustom || ShowKeyField ? "Save key" : "Continue"` IsEnabled=`!Working`
                                @Click+=`await SubmitAsync()` />
                    </StackPanel>
                </StackPanel>
                if (`Page == 3`)
                {
                    <StackPanel Spacing=10>
                        <TextBlock Text=`$"Authorize {ProviderName}"` FontSize=14 />
                        <TextBlock Text=`OauthInstructions` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                        <Button HorizontalAlignment=Left Content="Open browser" @Click+=`OpenOauthUrl()` IsEnabled=`!Working` />
                        if (`OauthNeedsCode`)
                            <TextBox Text<=>`Code` PlaceholderText="Authorization code" Height=32 VerticalContentAlignment=Center />
                        else
                            <TextBlock Text="Waiting for authorization…" FontSize=12 Foreground=`theme.SecondaryText` />
                        <StackPanel Orientation=Horizontal Spacing=10 HorizontalAlignment=Right>
                            <Button Content="Back" @Click+=`GoBack()` />
                            if (`Working`)
                                <ProgressRing Width=16 Height=16 VerticalAlignment=Center />
                            <Button Content=`OauthNeedsCode ? "Complete" : "I've finished authorizing"` IsEnabled=`!Working`
                                    @Click+=`await CompleteOauthAsync()` />
                        </StackPanel>
                    </StackPanel>
                }
                </StackPanel>
            </Border>

            <TextBlock Grid.Row=2 Margin=`new Thickness(0, 10, 0, 6)` Text=`Status` TextWrapping=Wrap FontSize=12
                       Foreground=`StatusError ? theme.SystemCritical : theme.SecondaryText`
                       Visibility=`Status.Length > 0 ? Visibility.Visible : Visibility.Collapsed` />
            </Grid>
        </ContentDialog>
    </root>
    """)]
public partial class ProviderConnectDialog : IQuickMarkupComponent<ContentDialog>
{
    /// <summary>Raised after a credential is stored (and the model options refreshed) — hide and close the dialog.</summary>
    public event Action? Completed;

    /// <summary>Router store that owns the client plus the model-option refresh + toast surfaces.</summary>
    public ChatStore? Store { get; set; }

    // Current method's prompt definition and the collected answers.
    private AuthPrompt[] _prompts = Array.Empty<AuthPrompt>();
    private string _currentMethodType = "";
    private readonly Dictionary<string, string> _inputs = new();
    private readonly List<TextBox> _textBoxes = new();
    private string _oauthUrl = "";

    /// <summary>
    /// Loads and shows the connect-provider dialog in <paramref name="xamlRoot"/> (shared entry
    /// point: the model picker's "Connect a provider…" row and the composer's /connect built-in).
    /// No-op when not connected to a server.
    /// </summary>
    public static async Task ShowAsync(ChatStore store, XamlRoot xamlRoot)
    {
        if (store.Client is null || xamlRoot is null) return;
        var dialog = new ProviderConnectDialog { Store = store };
        await dialog.LoadAsync();

        dialog.MarkupNode.XamlRoot = xamlRoot;
        dialog.Completed += () => dialog.MarkupNode.Hide();
        await dialog.MarkupNode.ShowAsync();
    }

    /// <summary>Fetches the provider catalog + auth methods into the list (call once before showing).</summary>
    public async Task LoadAsync()
    {
        if (Store?.Client is not { } client)
        {
            LoadError = "Not connected to a server.";
            Loading = false;
            return;
        }

        Loading = true;
        LoadError = null;
        Status = "";
        try
        {
            var list = await client.GetProvidersAsync();
            if (list is null)
            {
                LoadError = "Could not load providers.";
                return;
            }
            var connected = new HashSet<string>(list.Connected ?? new List<string>());
            Providers.Clear();
            var rows = (list.All ?? new List<ProviderInfo>())
                .Where(p => p.Id.Length > 0)
                .Select(p => new ProviderRow(p.Id, p.Name.Length > 0 ? p.Name : p.Id, connected.Contains(p.Id)))
                .OrderByDescending(r => r.IsConnected)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows) Providers.Add(row);

            _methodsResult = await client.GetProviderAuthMethodsAsync();
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        finally
        {
            Loading = false;
        }
    }

    // ── Navigation ──────────────────────────────────────────────────────────────

    private void GoBack()
    {
        Working = false;
        switch (Page)
        {
            case 1:
                Page = 0;
                Title = "";
                break;
            case 2:
                if (IsCustom)
                {
                    Page = 0;
                    Title = "";
                    IsCustom = false;
                }
                else
                {
                    Page = 1;
                }
                break;
            default:
                Page = 2;
                break;
        }
    }

    private void SelectProvider(ProviderRow p)
    {
        ProviderId = p.Id;
        ProviderName = p.Name;
        IsCustom = false;
        Title = $"Connect {p.Name}";
        Status = "";
        StatusError = false;
        Working = false;
        var methods = _methodsResult.TryGetValue(p.Id, out var found) && found.Length > 0
            ? found
            : new[] { new ProviderAuthMethod { Type = "api", Label = "API key" } };
        if (methods.Length == 1)
        {
            BeginMethod(0, methods);
        }
        else
        {
            Methods = methods.ToList();
            Page = 1;
        }
    }

    private void BeginCustom()
    {
        IsCustom = true;
        ProviderId = "";
        ProviderName = "Custom provider";
        Title = "Connect a custom provider";
        PromptCaption = "This stores a credential for a custom provider id. Configure the provider in opencode.json to use it.";
        _currentMethodType = "api";
        _prompts = Array.Empty<AuthPrompt>();
        ApiKey = "";
        CustomId = "";
        Code = "";
        Status = "";
        StatusError = false;
        Working = false;
        ShowPrompts = false;
        ShowKeyField = true;
        _inputs.Clear();
        Page = 2;
    }

    private void BeginMethod(int index) => BeginMethod(index, Methods.ToArray());

    private void BeginMethod(int index, ProviderAuthMethod[] methods)
    {
        var method = methods[index];
        MethodIndex = index;
        _currentMethodType = method.Type;
        _prompts = (method.Prompts ?? new List<AuthPrompt>()).ToArray();
        PromptCaption = $"{method.Label} · {ProviderName}";
        Title = $"Connect {ProviderName}";
        ApiKey = "";
        Code = "";
        Status = "";
        StatusError = false;
        Working = false;
        ShowPrompts = _prompts.Length > 0;
        ShowKeyField = method.Type == "api";
        _inputs.Clear();
        RebuildPromptPanel();
        Page = 2;
    }

    // ── Prompt inputs (text/select, honoring AuthWhen gating) ──────────────────

    private void RebuildPromptPanel()
    {
        if (formPromptHost is null) return;

        // Preserve anything already typed so a select-driven rebuild doesn't lose it.
        foreach (var textBox in _textBoxes)
        {
            var key = (string?)textBox.Tag;
            if (key is not null && textBox.Text.Length > 0) _inputs[key] = textBox.Text;
        }
        _textBoxes.Clear();
        formPromptHost.Children.Clear();

        foreach (var prompt in _prompts)
        {
            if (prompt.When is { } when)
            {
                if (!_inputs.TryGetValue(when.Key, out var whenValue) || whenValue.Length == 0) continue;
                var matches = when.Op == "eq" ? whenValue == when.Value : whenValue != when.Value;
                if (!matches) continue;
            }

            StackPanel? control;
            if (prompt.Type == "select")
            {
                var options = (prompt.Options ?? new List<AuthPromptOption>()).ToArray();
                var box = new ComboBox
                {
                    ItemsSource = options.Select(o => o.Label).ToList(),
                    Height = 32,
                    MinWidth = 320,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                };
                if (_inputs.TryGetValue(prompt.Key, out var value))
                {
                    var index = Array.FindIndex(options, o => o.Value == value);
                    if (index >= 0) box.SelectedIndex = index;
                }
                var captured = options;
                box.SelectionChanged += (_, _) =>
                {
                    if (box.SelectedIndex < 0) return;
                    _inputs[prompt.Key] = captured[box.SelectedIndex].Value;
                    RebuildPromptPanel();
                };
                control = MakeLabeledControl(prompt.Message, box);
            }
            else
            {
                var box = new TextBox
                {
                    PlaceholderText = prompt.Placeholder ?? "",
                    Height = 32,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Tag = prompt.Key,
                };
                if (_inputs.TryGetValue(prompt.Key, out var value)) box.Text = value;
                _textBoxes.Add(box);
                control = MakeLabeledControl(prompt.Message, box);
            }

            formPromptHost.Children.Add(control);
        }
    }

    private static StackPanel MakeLabeledControl(string label, Control control) => new()
    {
        Spacing = 4,
        Children =
        {
            new TextBlock { Text = label, FontSize = 11, Foreground = ThemeBrushes.Global.SecondaryText },
            control,
        },
    };

    /// <summary>Collects every answered prompt into a dictionary, ready for metadata/inputs.</summary>
    private Dictionary<string, string> CollectInputs()
    {
        foreach (var textBox in _textBoxes)
        {
            var key = (string?)textBox.Tag;
            if (key is not null && textBox.Text.Length > 0) _inputs[key] = textBox.Text;
        }
        return new Dictionary<string, string>(_inputs);
    }

    // ── Actions ─────────────────────────────────────────────────────────────────

    private async Task SubmitAsync()
    {
        if (Working) return;
        Working = true;
        Status = "";
        StatusError = false;
        try
        {
            if (Store?.Client is not { } client)
            {
                Status = "Not connected to a server.";
                StatusError = true;
                return;
            }

            if (IsCustom)
            {
                var providerId = CustomId.Trim().Replace("^@ai-sdk/", "");
                if (!IsValidCustomProviderId(providerId))
                {
                    Status = "Provider ids must start with a lowercase letter or number and only use lowercase letters, numbers, hyphens, and underscores.";
                    StatusError = true;
                    return;
                }
                ProviderId = providerId;
                ProviderName = providerId;
                await client.SetAuthAsync(providerId, ApiKey.Trim());
                await FinishAsync();
                return;
            }

            var inputs = CollectInputs();
            if (_currentMethodType == "api")
            {
                if (ApiKey.Trim().Length == 0)
                {
                    Status = "Enter an API key.";
                    StatusError = true;
                    return;
                }
                await client.SetAuthAsync(ProviderId, ApiKey.Trim(), inputs.Count > 0 ? inputs : null);
                await FinishAsync();
                return;
            }

            // OAuth: authorize returns the URL + whether a code is needed; the callback completes it on page 3.
            var result = await client.AuthorizeOAuthAsync(ProviderId, MethodIndex, inputs.Count > 0 ? inputs : null);
            if (result is null)
            {
                Status = "Authorization failed. Try again.";
                StatusError = true;
                return;
            }
            _oauthUrl = result.Url;
            OauthInstructions = result.Instructions.Length > 0
                ? result.Instructions
                : "Open the URL below, authorize the provider in your browser, then return here.";
            OauthNeedsCode = result.Method == "code";
            Code = "";
            Page = 3;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            StatusError = true;
        }
        finally
        {
            Working = false;
        }
    }

    private async Task CompleteOauthAsync()
    {
        if (Working) return;
        Working = true;
        Status = "";
        StatusError = false;
        try
        {
            if (Store?.Client is not { } client)
            {
                Status = "Not connected to a server.";
                StatusError = true;
                return;
            }
            await client.CompleteOAuthAsync(ProviderId, MethodIndex, OauthNeedsCode ? Code.Trim() : null);
            await FinishAsync();
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            StatusError = true;
        }
        finally
        {
            Working = false;
        }
    }

    private void OpenOauthUrl()
    {
        var error = FolderLauncher.OpenUrl(_oauthUrl);
        if (error is not null)
        {
            Status = $"Could not open a browser: {error}";
            StatusError = true;
        }
    }

    private static readonly System.Text.RegularExpressions.Regex CustomProviderIdRegex =
        new("^[a-z0-9][a-z0-9-_]*$");

    private static bool IsValidCustomProviderId(string id) => CustomProviderIdRegex.IsMatch(id);

    /// <summary>Refreshes the model/option lists, surfaces a toast, and tells the host to close.</summary>
    private async Task FinishAsync()
    {
        try
        {
            if (Store is { Client: not null }) await Store.RefreshSettingsAsync();
        }
        catch { /* The connect already succeeded; a failed refresh shouldn't undo it. */ }
        Store?.ShowToast(new ToastItem
        {
            Message = $"Connected to {ProviderName}",
            Variant = "success",
            DurationMs = 4000,
        });
        Completed?.Invoke();
    }

    // ── Filtering ────────────────────────────────────────────────────────────────

    private static IEnumerable<ProviderRow> FilterProviders(ObservableCollection<ProviderRow> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        var q = query.Trim();
        return source.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Id.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, ProviderAuthMethod[]> _methodsResult = new();
}

/// <summary>A row in the provider list: id, display name, and whether a credential is stored.</summary>
public sealed class ProviderRow
{
    public ProviderRow(string id, string name, bool isConnected)
    {
        Id = id;
        Name = name;
        IsConnected = isConnected;
    }

    public string Id { get; }

    public string Name { get; }

    public bool IsConnected { get; }
}