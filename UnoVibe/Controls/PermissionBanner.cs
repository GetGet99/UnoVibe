namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required PermissionRequestItem Request;
    string Stage = "choose";
    string RejectText = "";
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <Border Background=`theme.CardBackground` CornerRadius=8 Padding=`new Thickness(12, 10, 12, 10)` Margin=`new Thickness(0, 8, 0, 0)`
            BorderBrush=`theme.SystemCaution` BorderThickness=`new Thickness(1)` MaxWidth=640 HorizontalAlignment=Left>
        <StackPanel Spacing=8>
            <StackPanel Spacing=2>
                <TextBlock Text=`Request.Title` FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap IsTextSelectionEnabled=true />
                if (`Request.Body`)
                    <TextBlock Text=`Request.Body` FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
                if (`Request.PatternsText`)
                    <TextBlock Text=`Request.PatternsText` FontSize=10 Foreground=`theme.TertiaryText` TextWrapping=Wrap IsTextSelectionEnabled=true />
            </StackPanel>
            if (`Stage == "reject"`)
                <StackPanel Spacing=8>
                    <TextBox Text<=>`RejectText` PlaceholderText="Reason for rejection (optional)" AcceptsReturn=false MinHeight=36 />
                    <StackPanel Orientation=Horizontal Spacing=8>
                        <Button Content="Cancel" @Click+=`CancelReject()` CornerRadius=6 />
                        <Button Content="Deny" @Click+=`await RejectWithReasonAsync()` CornerRadius=6 />
                    </StackPanel>
                </StackPanel>
            else
                <StackPanel Orientation=Horizontal Spacing=8>
                    <Button Content="Allow once" @Click+=`await AllowOnceAsync()` CornerRadius=6 />
                    <Button Content="Always allow" @Click+=`await AllowAlwaysAsync()` CornerRadius=6 />
                    <Button Content="Deny…" @Click+=`StartReject()` CornerRadius=6 />
                </StackPanel>
        </StackPanel>
    </Border>
    """)]
partial class PermissionBanner : IQuickMarkupComponent
{
    public delegate Task AllowHandler(string requestId);

    public event AllowHandler? AllowOnceRequested;

    public event AllowHandler? AllowAlwaysRequested;

    public delegate Task RejectHandler(string requestId, string? message);

    public event RejectHandler? RejectRequested;

    private Task AllowOnceAsync() =>
        AllowOnceRequested?.Invoke(Request.Id) ?? Task.CompletedTask;

    private Task AllowAlwaysAsync() =>
        AllowAlwaysRequested?.Invoke(Request.Id) ?? Task.CompletedTask;

    private void StartReject() => Stage = "reject";

    private void CancelReject() => Stage = "choose";

    private async Task RejectWithReasonAsync()
    {
        var message = RejectText.Trim();
        if (RejectRequested is { } handler)
            await handler(Request.Id, message.Length > 0 ? message : null);
        Stage = "choose";
        RejectText = "";
    }

    [QuickMarkupConstructor]
    private void Ctor()
    {
        var lastId = Request.Id;
        RequestProp.Watch(req =>
        {
            if (req?.Id != lastId)
            {
                lastId = req?.Id ?? "";
                Stage = "choose";
                RejectText = "";
            }
        });
        Init();
    }
}
