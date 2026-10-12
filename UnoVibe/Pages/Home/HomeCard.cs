namespace UnoVibe.Pages.Home;

[QuickMarkup("""
    using UnoVibe.Controls;
    using UnoVibe.Pages.Chat;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    required HomeCardSnapshot Card;
    inject HomeProvider Home;
    inject SessionsStateProvider Sessions;
    inject OpencodeConnection Connection;
    inject OpencodeClient Opencode;
    inject bool IsCompact;
    SessionHead? Head => `Sessions.Head(Card.SessionId)`;
    string? Branch => `Head?.Directory is {} dir ? Sessions.BranchOf(dir) : null`;
    string DirLabel => `Head is {} h ? PathDisplayHelper.Relative(h.Directory, Connection.ServerDirectory) : ""`;
    string TitleText => `Head?.Title ?? "Session"`;
    string TimeLabel => `Head?.TimeLabel ?? ""`;
    bool IsBusy => `Head?.IsBusy ?? false`;
    SessionState CardState => `Head?.State ?? SessionState.None`;
    <setup>
        var theme = ThemeBrushes.Global;
        var transparent = new SolidColorBrush(Colors.Transparent);
    </setup>
    <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=`new Thickness(1, 1, 1, 1)` CornerRadius=8 Padding=`new Thickness(12, 10, 12, 10)`>
        <StackPanel Spacing=8>
            <Grid ColumnSpacing=8 ColumnDefinitions=<>
                <ColumnDefinition Width=Auto />
                <ColumnDefinition />
                <ColumnDefinition Width=Auto />
            </>>
                if (`IsBusy`)
                {
                    <ProgressRing Grid.Column=0 Width=36 Height=36 IsActive=true HorizontalAlignment=Center VerticalAlignment=Center />
                }
                else
                {
                    <Border Grid.Column=0 Width=36 Height=36 CornerRadius=18 Background=`theme.SolidBackground` BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(1, 1, 1, 1)` VerticalAlignment=Center>
                        <AppSymbolIcon Symbol=`HomeSymbol(CardState)` FontSize=16 Foreground=`HomeBrush(CardState)` HorizontalAlignment=Center VerticalAlignment=Center />
                    </Border>
                }
                <StackPanel Grid.Column=1 Spacing=2 VerticalAlignment=Center>
                    <StackPanel Orientation=Horizontal Spacing=6>
                        <TextBlock Text=`DirLabel` FontSize=11 Foreground=`theme.SecondaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center ToolTipService.ToolTip=`Head?.Directory ?? ""` />
                        if (`Branch`)
                        {
                            <TextBlock Text=`$"⎇ {Branch}"` FontSize=10 Foreground=`theme.TertiaryText` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                        }
                        if (`TimeLabel`)
                        {
                            <TextBlock Text=`$"· {TimeLabel}"` FontSize=10 Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                        }
                    </StackPanel>
                    <TextBlock Text=`TitleText` FontSize=13 FontWeight=`FontWeights.SemiBold` TextTrimming=`TextTrimming.CharacterEllipsis` VerticalAlignment=Center />
                    <ChatCostInline Cost=`Card.Cost` Tokens=`Card.Tokens` ContextLimit=`Card.ContextLimit` />
                </StackPanel>
                <StackPanel Grid.Column=2 Orientation=Horizontal Spacing=4 VerticalAlignment=Top>
                    <Button Padding=`new Thickness(6, 4, 6, 4)` @Click+=`OpenSession()` ToolTipService.ToolTip="Open session">
                        <AppSymbolIcon Symbol=View FontSize=11 />
                    </Button>
                    if (`Head is not null`)
                    {
                        <FolderActions Directory=`Head!.Directory` ShowNewSession=false />
                    }
                    <Button Padding=`new Thickness(6, 4, 6, 4)` @Click+=`Home.Unpin(Card.SessionId)` ToolTipService.ToolTip="Remove from Home">
                        <AppSymbolIcon Symbol=Accept FontSize=11 />
                    </Button>
                </StackPanel>
            </Grid>
            if (`Card.IsLoading`)
            {
                <ProgressRing Width=20 Height=20 IsActive=true HorizontalAlignment=Left />
            }
            else
            {
                foreach (var m in `Card.PreviewMessages`; `m.Id`)
                {
                    <MessageView Message=`m` PreviewMode=true VisiblePartIds=`Card.VisibleParts(m.Id)` QuestionAnswered+=`OnQuestionAnswered` QuestionRejected+=`OnQuestionRejected` />
                }
                if (`Card.TodoTotal > 0`)
                {
                    <TextBlock Text=`$"◐ {Card.TodoDone}/{Card.TodoTotal} todos"` FontSize=11 Foreground=`theme.SecondaryText` />
                }
            }
            if (`Card.ActivePermission`)
            {
                <PermissionBanner Request=`Card.ActivePermission!` AllowOnceRequested+=`OnAllowOnce` AllowAlwaysRequested+=`OnAllowAlways` RejectRequested+=`OnRejectPermission` />
            }
            if (`Head is not null`)
            {
                <ChatComposer Chatbox=`Sessions.EnsureChatbox(Card.SessionId)` ChatParams=`Head!.ChatParams`
                    SessionDirectory=`Head!.Directory` Head=`Head` />
            }
        </StackPanel>
    </Border>
    """)]
partial class HomeCard : IQuickMarkupComponent
{
    private void OpenSession() => Home.OpenSession(Card.SessionId);

    private Task OnQuestionAnswered(string requestId, IReadOnlyList<IReadOnlyList<string>> answers) =>
        Home.ReplyQuestionAsync(Card.SessionId, requestId, answers);

    private Task OnQuestionRejected(string requestId) =>
        Home.RejectQuestionAsync(Card.SessionId, requestId);

    private Task OnAllowOnce(string requestId) =>
        Home.ReplyPermissionAsync(Card.SessionId, requestId, "once");

    private Task OnAllowAlways(string requestId) =>
        Home.ReplyPermissionAsync(Card.SessionId, requestId, "always");

    private Task OnRejectPermission(string requestId, string? message) =>
        Home.ReplyPermissionAsync(Card.SessionId, requestId, "reject", message);

    static Symbol HomeSymbol(SessionState s) => s switch
    {
        SessionState.Success => Symbol.Accept,
        SessionState.Interrupted => Symbol.Stop,
        SessionState.Error => Symbol.Cancel,
        SessionState.PendingPermission => Symbol.Permissions,
        SessionState.PendingQuestion => Symbol.Help,
        _ => Symbol.Message,
    };

    static Brush? HomeBrush(SessionState s) => s switch
    {
        SessionState.Success => ThemeBrushes.Global.SystemSuccess,
        SessionState.Interrupted => ThemeBrushes.Global.SystemCaution,
        SessionState.Error => ThemeBrushes.Global.SystemCritical,
        SessionState.PendingPermission or SessionState.PendingQuestion => ThemeBrushes.Global.SystemAttention,
        _ => ThemeBrushes.Global.PrimaryText,
    };
}
