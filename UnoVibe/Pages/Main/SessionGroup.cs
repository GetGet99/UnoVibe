namespace UnoVibe.Pages.Main;

[QuickMarkup("""
    using UnoVibe.Controls;
    using Microsoft.UI;
    required SessionGroupModel Group;
    inject OpencodeConnection Connection;
    private bool ShowMore = false;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <StackPanel Margin=`new Thickness(0, 12, 0, 0)`>
        <Grid ColumnDefinitions=<>
            <ColumnDefinition />
            <ColumnDefinition Width=Auto />
        </> ColumnSpacing=4>
            <StackPanel Orientation=Horizontal Spacing=4>
                <TextBlock Text=`DisplayPath(Group.Directory)` FontSize=11 FontWeight=`FontWeights.SemiBold` Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                if (`Group.Branch`)
                {
                    <TextBlock Text=`$"⎇ {Group.Branch}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                }
            </StackPanel>
            // TODO: When attached property support falling back to element properly, do that instead of wrapping in Grid.
            <Grid Grid.Column=1 VerticalAlignment=Center>
                <FolderActions Directory=`Group.Directory` />
            </Grid>
        </Grid>
        if (`Group.Sessions.Count == 0`)
        {
            <TextBlock Text="No sessions yet" FontSize=11 Foreground=`theme.TertiaryText` Margin=`new Thickness(0, 6, 0, 0)` />
        }
        foreach (var s in `ShowMore ? Group.Sessions : Group.Sessions.Take(MaxVisibleSessions)`; `s.Id`)
        {
            <SessionButton Session=`s` />
        }
        if (`Group.Sessions.Count > MaxVisibleSessions`)
        {
            <Button Margin=`new Thickness(0, 4, 0, 0)` Padding=`new Thickness(8, 4, 8, 4)` HorizontalAlignment=Left Background=`transparent` BorderThickness=0 @Click+=`ShowMore = !ShowMore`>
                <TextBlock Text=`ShowMore ? "Show less" : $"Show more ({Group.Sessions.Count - MaxVisibleSessions})"` FontSize=11 Foreground=`theme.SecondaryText` />
            </Button>
        }
    </StackPanel>
    """)]
partial class SessionGroup : IQuickMarkupComponent
{

    /// <summary>
    /// Path relative to the connected server's directory via <see cref="PathDisplayHelper.Relative"/>.
    /// </summary>
    private string DisplayPath(string fullPath) => PathDisplayHelper.Relative(fullPath, Connection.ServerDirectory);
    /// <summary>Number of sessions shown per directory group before the "Show more" toggle appears.</summary>
    private const int MaxVisibleSessions = 5;
}