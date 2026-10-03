namespace UnoVibe.Pages.Chat;

[QuickMarkup("""
    using UnoVibe.States;
    using UnoVibe.Models;
    inject ChatMessagesState? ChatState;
    string CountdownText => `ComputeCountdown()`;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Spacing=6>
            <StackPanel Orientation=Horizontal Spacing=8>
                <ProgressRing Width=14 Height=14 IsActive=true VerticalAlignment=Center />
                <TextBlock Text="Auto-retrying" FontSize=12 FontWeight=`FontWeights.SemiBold` VerticalAlignment=Center />
            </StackPanel>
            if (`ChatState?.Retry.Message.Length > 0`)
                <TextBlock Text=`ChatState.Retry.Message` FontSize=12 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
            <TextBlock Text=`CountdownText` FontSize=11 Foreground=`theme.SystemCaution` TextWrapping=Wrap />
        </StackPanel>
    </root>
    """)]
partial class ChatRetryCard : IQuickMarkupComponent
{
    DispatcherTimer? countdown;

    [QuickMarkupConstructor]
    void Ctor()
    {
        countdown = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        countdown.Tick += (_, _) => CountdownTextComp.Invalidate();
        countdown.Start();
        Init();
    }

    string ComputeCountdown()
    {
        var retry = ChatState?.Retry;
        if (retry is null || !retry.IsRetrying) return "";
        if (retry.NextMs <= 0)
            return retry.Attempt > 0 ? $"Attempt #{retry.Attempt} · retrying…" : "Retrying…";
        var seconds = Math.Max(0, (int)Math.Ceiling((retry.NextMs - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000.0));
        return retry.Attempt > 0 ? $"Attempt #{retry.Attempt} · retrying in {seconds}s" : $"Retrying in {seconds}s";
    }
}
