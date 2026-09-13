using UnoVibe.Models;
using Windows.ApplicationModel.DataTransfer;

namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using QuickMarkup.Infra.Collections;
    using Microsoft.UI;
    bool ShowPassword = false;
    inject OpencodeConnection Connection;
    inject ToastsProvider Toasts;
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
                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` ToolTipService.ToolTip="Copy directory" @Click+=`CopyToClipboard("Directory", Connection.ServerDirectory)`>
                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                </Button>
            </Grid>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                <TextBlock Text="Server" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Grid.Column=1 Text=`Connection.BaseUrl` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Connection.BaseUrl` />
                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` ToolTipService.ToolTip="Copy URL" @Click+=`CopyToClipboard("URL", Connection.BaseUrl)`>
                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                </Button>
            </Grid>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
                <ColumnDefinition Width=Auto />
            </>>
                <TextBlock Text="Password" FontSize=12 Foreground=`theme.SecondaryText` VerticalAlignment=Center />
                <TextBlock Grid.Column=1 Text=`ShowPassword ? Connection.Password : MaskPassword(Connection.Password)` FontSize=12 IsTextSelectionEnabled=true TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`ShowPassword ? Connection.Password : "Hidden — click the eye to reveal"` />
                <Button Grid.Column=2 Padding=`new Thickness(6, 3, 6, 3)` Visibility=`Connection.Password.Length > 0 ? Visibility.Visible : Visibility.Collapsed` ToolTipService.ToolTip=`ShowPassword ? "Hide password" : "Show password"` @Click+=`ShowPassword = !ShowPassword`>
                    <AppSymbolIcon Symbol=View FontSize=11 />
                </Button>
                <Button Grid.Column=3 Padding=`new Thickness(6, 3, 6, 3)` Visibility=`Connection.Password.Length > 0 ? Visibility.Visible : Visibility.Collapsed` ToolTipService.ToolTip="Copy password" @Click+=`CopyToClipboard("Password", Connection.Password)`>
                    <AppSymbolIcon Symbol=Copy FontSize=11 />
                </Button>
            </Grid>
        </StackPanel>
    </Flyout>
    """)]
public partial class ConnectionFlyout : IQuickMarkupComponent<Flyout>
{

    /// <summary>
    /// Renders the connection password while hidden: a fixed-width bullet mask, or "None"
    /// when the server has no password. The real value is never shown by default.
    /// </summary>
    private static string MaskPassword(string password) =>
        password.Length == 0 ? "None" : "••••••••";

    /// <summary>Copies a connection value to the system clipboard and confirms with a toast.</summary>
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
