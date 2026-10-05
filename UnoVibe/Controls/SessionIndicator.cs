namespace UnoVibe.Controls;
[QuickMarkup("""
    SessionState State;
    <Grid Width=14 Height=14 VerticalAlignment=Center>
        if (`State is SessionState.Working`) {
            <ProgressRing Width=12 Height=12 IsActive HorizontalAlignment=Center VerticalAlignment=Center />
        } else {
            <AppSymbolIcon Symbol=`GetSymbol(State)` FontSize=10 Foreground=`GetBrush(State)` HorizontalAlignment=Center VerticalAlignment=Center />
        }
    </Grid>
    """)]
partial class SessionIndicator : IQuickMarkupComponent<Grid>
{
    private static Symbol GetSymbol(SessionState s) => s switch
    {
        SessionState.None => Symbol.Message,
        SessionState.Success => Symbol.Accept,
        SessionState.Interrupted => Symbol.Stop,
        SessionState.Error => Symbol.Cancel,
        SessionState.PendingPermission => Symbol.Permissions,
        SessionState.PendingQuestion => Symbol.Help,
        _ => Symbol.Message,
    };

    private static Brush? GetBrush(SessionState s) => s switch
    {
        SessionState.Success => ThemeBrushes.Global.SystemSuccess,
        SessionState.Interrupted => ThemeBrushes.Global.SystemCaution,
        SessionState.Error => ThemeBrushes.Global.SystemCritical,
        SessionState.PendingPermission or SessionState.PendingQuestion => ThemeBrushes.Global.SystemAttention,
        _ => ThemeBrushes.Global.PrimaryText,
    };
}