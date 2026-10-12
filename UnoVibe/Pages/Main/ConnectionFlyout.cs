using Windows.ApplicationModel.DataTransfer;

namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    bool ShowPassword = false;
    inject OpencodeConnection Connection;
    inject ToastsProvider Toasts;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Flyout Placement=Top @Closed+=`ShowPassword = false`>
        <StackPanel Spacing=10 MinWidth=320 MaxWidth=400>
            <TextBlock Text="Connection" FontSize=13 FontWeight=`FontWeights.SemiBold` />
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <TextBlock Text="Directory" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Grid.Column=1 Text=`Connection.ServerDirectory` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Connection.ServerDirectory` />
                <Border Grid.Column=2>
                    <SmallButtonIcon Symbol=Copy Tooltip="Copy directory" @Click+=`CopyToClipboard("Directory", Connection.ServerDirectory)` />
                </Border>
            </Grid>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <TextBlock Text="Server" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Grid.Column=1 Text=`Connection.BaseUrl` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Connection.BaseUrl` />
                <Border Grid.Column=2>
                    <SmallButtonIcon Symbol=Copy Tooltip="Copy URL" @Click+=`CopyToClipboard("URL", Connection.BaseUrl)` />
                </Border>
            </Grid>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
                <ColumnDefinition Width=Auto />
            </>>
                <TextBlock Text="Password" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Grid.Column=1 Text=`ShowPassword ? Connection.Password : MaskPassword(Connection.Password)` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`ShowPassword ? Connection.Password : "Hidden — click the eye to reveal"` />
                <Border Grid.Column=2>
                    <SmallButtonIcon Symbol=View Tooltip=`ShowPassword ? "Hide password" : "Show password"` Visibility=`!string.IsNullOrEmpty(Connection.Password) ? Visibility.Visible : Visibility.Collapsed` @Click+=`ShowPassword = !ShowPassword` />
                </Border>
                <Border Grid.Column=3>
                    <SmallButtonIcon Symbol=Copy Tooltip="Copy password" Visibility=`!string.IsNullOrEmpty(Connection.Password) ? Visibility.Visible : Visibility.Collapsed` @Click+=`CopyToClipboard("Password", Connection.Password)` />
                </Border>
            </Grid>
        </StackPanel>
    </Flyout>
    """)]
partial class ConnectionFlyout : IQuickMarkupComponent<Flyout>
{

    private static string MaskPassword(string? password) =>
        string.IsNullOrEmpty(password) ? "None" : "••••••••";

    private void CopyToClipboard(string label, string text)
    {
        var data = new DataPackage();
        data.SetText(text);
        Clipboard.SetContent(data);
        Toasts.Show(new ToastItem
        {
            Title = "Copied",
            Message = $"{label} copied to clipboard.",
            Variant = "success",
        });
    }
}
