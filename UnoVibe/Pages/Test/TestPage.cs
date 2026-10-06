namespace UnoVibe.Pages.Test;

[QuickMarkup("""
    using UnoVibe.Controls.ToolViews;
    using QuickMarkup.WinUI;
    ToolCallPartItem Part = `TestEditParts.CreatePending()`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Page>
        <ScrollViewer VerticalScrollBarVisibility=Auto>
            <StackPanel MaxWidth=880 Padding=`new Thickness(24, 20, 24, 20)` Spacing=12 HorizontalAlignment=Center>
                <TextBlock Text="Edit tool view harness" FontSize=20 FontWeight=`FontWeights.SemiBold` Foreground=`theme.PrimaryText` TextWrapping=Wrap />
                <TextBlock Text="Walks one edit tool call through pending, running, and completed without a live agent." FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                <TextBlock Text=`$"Status: {Part.ToolStatus} · Busy: {Part.IsBusy} · Diff: {(Part.Diff is null ? "absent" : "present")}"` FontSize=12 FontFamily=`CodeFontsHelper.Current` Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                <ToolViewEdit Part=`Part` />
                <StackPanel Orientation=Horizontal Spacing=8>
                    <Button Content="Mark running" @Click+=`MarkRunning()` />
                    <Button Content="Complete" @Click+=`MarkCompleted()` />
                    <Button Content="Reset" @Click+=`ResetPart()` />
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </Page>
    """)]
partial class TestPage : IQuickMarkupComponent<Page>
{
    public const string EnvVar = "UNOVIBE_TEST_PAGE";

    public static bool IsEnabled
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable(EnvVar);
            return string.Equals(raw, "1", StringComparison.Ordinal)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void MarkRunning() => TestEditParts.MarkRunning(Part);

    private void MarkCompleted() => TestEditParts.MarkCompleted(Part);

    private void ResetPart() => Part = TestEditParts.CreatePending();
}
