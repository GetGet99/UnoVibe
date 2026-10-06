namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Media;
    required string Title;
    bool Expanded = false;
    bool Enabled = true;
    bool ShowSpinner = false;
    Brush? SpinnerForeground;
    Brush? TitleForeground;
    Action? Toggle;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border>
        if (`Enabled`)
            <Button CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Toggle?.Invoke()`>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <ProgressRing Width=14 Height=14 IsActive=true Foreground=`SpinnerForeground ?? theme.SystemCaution` VerticalAlignment=Center Visibility=`ShowSpinner ? Visibility.Visible : Visibility.Collapsed` />
                    <TextBlock Text=`Expanded ? "▾" : "▸"` FontSize=12 Foreground=`TitleForeground` VerticalAlignment=Center />
                    <TextBlock Text=`Title` FontSize=12 Foreground=`TitleForeground` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                </StackPanel>
            </Button>
        else
            <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Stretch>
                <ProgressRing Width=14 Height=14 IsActive=true Foreground=`SpinnerForeground ?? theme.SystemCaution` VerticalAlignment=Center Visibility=`ShowSpinner ? Visibility.Visible : Visibility.Collapsed` />
                <TextBlock Text=`Title` FontSize=12 Foreground=`TitleForeground` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
    </Border>
    """)]
partial class AccordionHeader : IQuickMarkupComponent<Border>;
