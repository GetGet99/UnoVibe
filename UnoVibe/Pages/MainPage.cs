using Microsoft.UI.Xaml.Media;
using QuickMarkup.WinUI;
using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Pages;

/// <summary>
/// Root page: session sidebar on the left, chat page on the right.
/// Also hosts the top-right toast overlay (from <c>tui.toast.show</c> events).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using UnoVibe.Pages;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    provide ChatStore Store = null;
    provide Window HostWindow = null;
    provide bool SettingsOpen = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Grid>
            <Grid ColumnDefinitions=<>
                <ColumnDefinition Width=280 />
                <ColumnDefinition />
            </>>
                <SessionSidebar />
                <ChatPage Grid.Column=1 />
            </Grid>
            <Grid HorizontalAlignment=Stretch VerticalAlignment=Stretch Padding=`new Thickness(16, 12, 16, 0)`>
                if (`Store.CurrentToast is not null`)
                    <Border HorizontalAlignment=Right VerticalAlignment=Top MaxWidth=440 CornerRadius=8
                            Padding=`new Thickness(14, 10, 14, 10)`
                            Background=`ToastBackground(Store.CurrentToast)`
                            BorderBrush=`ToastAccent(Store.CurrentToast)` BorderThickness=`new Thickness(1, 1, 3, 1)`>
                        <Grid ColumnSpacing=8 ColumnDefinitions=<>
                            <ColumnDefinition />
                            <ColumnDefinition Width=Auto />
                        </>>
                            <StackPanel Grid.Column=0 Spacing=4>
                                if (`(Store.CurrentToast?.Title?.Length ?? 0) > 0`)
                                    <TextBlock Text=`Store.CurrentToast?.Title ?? ""` FontSize=13 FontWeight=`FontWeights.SemiBold`
                                               Foreground=`ToastAccent(Store.CurrentToast)` TextWrapping=Wrap IsTextSelectionEnabled=true />
                                <TextBlock Text=`Store.CurrentToast?.Message ?? ""` FontSize=12 Foreground=`theme.PrimaryText`
                                           TextWrapping=Wrap IsTextSelectionEnabled=true />
                            </StackPanel>
                            <Button Grid.Column=1 Content="✕" Width=24 Height=24 Padding=0 Margin=`new Thickness(0, -4, -4, 0)`
                                    VerticalAlignment=Top @Click+=`Store.DismissToast()` ToolTipService.ToolTip="Dismiss" />
                        </Grid>
                    </Border>
            </Grid>
            if (`SettingsOpen`)
            {
                <Grid>
                    <Border Background=`new SolidColorBrush(Colors.Black) { Opacity = 0.35 }` />
                    <Border Width=600 MaxHeight=640 VerticalAlignment=Center HorizontalAlignment=Center CornerRadius=10
                            Background=`theme.SolidBackground` BorderBrush=`theme.CardStroke` BorderThickness=1>
                        <SettingsPage />
                    </Border>
                </Grid>
            }
        </Grid>
    </root>
    """)]
public partial class MainPage : Page
{
    public void ProvideStore(ChatStore store) => Store = store;

    /// <summary>Host window of this page, used to init WinRT pickers/dialogs with an HWND.</summary>
    public void ProvideWindow(Window window) => HostWindow = window;

    private static Brush? ToastAccent(ToastItem? toast) => toast?.Variant switch
    {
        "success" => ThemeBrushes.Global.SystemSuccess,
        "warning" => ThemeBrushes.Global.SystemCaution,
        "error" => ThemeBrushes.Global.SystemCritical,
        _ => ThemeBrushes.Global.SystemAttention,
    };

    private static Brush? ToastBackground(ToastItem? toast) => toast?.Variant switch
    {
        "success" => ThemeBrushes.Global.SystemSuccessBackground,
        "warning" => ThemeBrushes.Global.SystemCautionBackground,
        "error" => ThemeBrushes.Global.SystemCriticalBackground,
        _ => ThemeBrushes.Global.SystemAttentionBackground,
    };
}