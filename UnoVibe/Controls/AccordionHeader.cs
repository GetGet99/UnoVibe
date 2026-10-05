namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI.Xaml.Media;
    required string Title;
    bool Expanded = false;
    bool Enabled = true;
    bool SemiBold = false;
    bool ShowSpinner = false;
    Brush? SpinnerForeground;
    Brush? TitleForeground;
    Action? Toggle;
    bool Hovering = false;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        if (`Enabled`)
            <Button Background=`Hovering ? theme.SystemNeutralBackground : theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)` BorderThickness=0 HorizontalContentAlignment=Left HorizontalAlignment=Stretch Click+=`(s, e) => Toggle?.Invoke()` PointerEntered+=`(s, e) => Hovering = true` PointerExited+=`(s, e) => Hovering = false`>
                <StackPanel Orientation=Horizontal Spacing=8>
                    <ProgressRing Width=14 Height=14 IsActive=true Foreground=`SpinnerForeground ?? theme.SystemCaution` VerticalAlignment=Center Visibility=`ShowSpinner ? Visibility.Visible : Visibility.Collapsed` />
                    <TextBlock Text=`Expanded ? "▾" : "▸"` FontSize=12 Foreground=`Hovering ? theme.PrimaryText : theme.SecondaryText` VerticalAlignment=Center />
                    <TextBlock Text=`Title` FontSize=12 FontWeight=`SemiBold ? FontWeights.SemiBold : FontWeights.Normal` Foreground=`TitleForeground ?? theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
                </StackPanel>
            </Button>
        else
            <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Stretch>
                <ProgressRing Width=14 Height=14 IsActive=true Foreground=`SpinnerForeground ?? theme.SystemCaution` VerticalAlignment=Center Visibility=`ShowSpinner ? Visibility.Visible : Visibility.Collapsed` />
                <TextBlock Text=`Title` FontSize=12 FontWeight=`SemiBold ? FontWeights.SemiBold : FontWeights.Normal` Foreground=`TitleForeground ?? theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true VerticalAlignment=Center />
            </StackPanel>
    </root>
    """)]
partial class AccordionHeader : IQuickMarkupComponent<UIElement>;
