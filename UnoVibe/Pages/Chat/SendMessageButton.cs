namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using Microsoft.UI.Xaml;
    using Microsoft.UI.Xaml.Controls;
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
partial class SendMessageButton : IQuickMarkupComponent<ContentControl>
{
    public delegate Task SendModeHandler(SendPromptMode mode);

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

    private string SendTooltip => $"Send ({ModeLabel(Mode)})";

    private static string ModeLabel(string mode) => mode switch
    {
        "Queue" => "queue until idle",
        "SendImmediately" => "send immediately",
        _ => "on next tool call",
    };
}