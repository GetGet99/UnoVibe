using UnoVibe.Models;

namespace UnoVibe.Pages.Main;

/// <summary>
/// Root page: session sidebar on the left, chat page on the right.
/// Also hosts the top-right toast overlay (from <c>tui.toast.show</c> events).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Integration;
    using UnoVibe.Services;
    using UnoVibe.Pages.Chat;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject bool IsCompact;
    inject bool IsSidebarView;
    inject bool SettingsOpen;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Grid>
        // On wide windows the sidebar and chat sit side by side (the flag is ignored). On compact
        // windows only one of them is shown at a time — the flag picks which — since there's no room
        // for both. The hidden panel is Collapsed (not just 0-width) so it doesn't lay out at all.
        <Grid ColumnDefinitions=<>
            <ColumnDefinition Width=`!IsCompact ? new GridLength(280) : (IsSidebarView ? new GridLength(1, GridUnitType.Star) : new GridLength(0))` />
            <ColumnDefinition Width=`IsSidebarView ? new GridLength(0) : new GridLength(1, GridUnitType.Star)` />
        </>>
            <SessionSidebar Visibility=`!IsCompact || IsSidebarView ? Visibility.Visible : Visibility.Collapsed` />
            <ChatPage Grid.Column=1 Visibility=`!IsCompact || !IsSidebarView ? Visibility.Visible : Visibility.Collapsed` />
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
    """)]
public partial class MainContent : IQuickMarkupComponent<Grid>
{

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
