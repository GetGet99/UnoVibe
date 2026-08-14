using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>
/// The composer's send button. When the session is idle (<see cref="IsBusy"/> false) it is a plain
/// send button that sends immediately. While a turn runs (<see cref="IsBusy"/> true) it becomes a
/// <see cref="SplitButton"/> whose primary click sends with the configured default send mode
/// (<see cref="SettingsStore.SendMode"/>) and whose chevron opens a menu of one-shot alternative
/// send modes — so the composer keeps working mid-turn without silently losing the message.
///
/// The button keeps the plain send-icon look in both states; a tooltip states the active default
/// mode. Menu picks (<see cref="PickMode"/>) are <b>one-time overrides</b> — they send with the
/// chosen mode but never change <see cref="SettingsStore.SendMode"/>, so the primary stays the
/// configured default. <see cref="Mode"/> (the configured default, kept fresh by the consumer via
/// <see cref="SettingsStore.Changed"/>) drives the tooltip.
/// </summary>
[QuickMarkup("""
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
    using UnoVibe.Services;
    using QuickMarkup.WinUI;
    public string Mode = "";
    public bool IsBusy = false;
    public bool Enabled = true;
    <root>
        if (`IsBusy`)
            <SplitButton IsEnabled=`Enabled` ToolTipService.ToolTip=`SendTooltip`
                    @Click+=`OnPrimaryClick()` Flyout=sendMenu=<MenuFlyout Placement=BottomEdgeAlignedRight>
                <MenuFlyoutItem Text="On next tool call" @Click+=`PickMode(SendPromptMode.OnNextToolCall)` />
                <MenuFlyoutItem Text="Queue until idle" @Click+=`PickMode(SendPromptMode.Queue)` />
                <MenuFlyoutItem Text="Send immediately" @Click+=`PickMode(SendPromptMode.SendImmediately)` />
            </MenuFlyout>>
                <SymbolIcon Symbol=Send VerticalAlignment=Center />
            </SplitButton>
        else
            <Button IsEnabled=`Enabled` ToolTipService.ToolTip=`SendTooltip` @Click+=`OnPlainClick()`>
                <SymbolIcon Symbol=Send VerticalAlignment=Center />
            </Button>
    </root>
    """)]
public partial class SendMessageButton : IQuickMarkupComponent<ContentControl>
{
    /// <summary>Handler for <see cref="SendRequested"/>.</summary>
    public delegate Task SendModeHandler(SendPromptMode mode);

    /// <summary>
    /// Raised with the mode to use for this send: the configured default for a primary/plain click
    /// (<see cref="SettingsStore.SendMode"/>), or the chosen menu item for a one-time override.
    /// </summary>
    public event SendModeHandler? SendRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }

    private void OnPrimaryClick() => _ = SendRequested?.Invoke(SettingsStore.SendMode);

    private void OnPlainClick() => _ = SendRequested?.Invoke(SettingsStore.SendMode);

    private void PickMode(SendPromptMode mode)
    {
        if (sendMenu is { IsOpen: true }) sendMenu.Hide();
        _ = SendRequested?.Invoke(mode);
    }

    /// <summary>Tooltip on the button; the mode is read fresh so it always reflects the setting.</summary>
    private string SendTooltip => $"Send ({ModeLabel(Mode)})";

    private static string ModeLabel(string mode) => mode switch
    {
        "Queue" => "queue until idle",
        "SendImmediately" => "send immediately",
        _ => "on next tool call",
    };
}