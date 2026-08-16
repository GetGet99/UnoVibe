using UnoVibe.Services;

namespace UnoVibe.Pages.Connect;

/// <summary>
/// The right column of the connect page: "Start a session" (Open Folder, Connect to URL with
/// its inline form) and the folder-security block — the single source of truth for folder
/// passwords. All password/URL/connect-form state is injected from the page so the values are
/// shared (and restored) there; connecting an action is delegated via
/// <see cref="OpenFolderRequested"/> / <see cref="ConnectToUrlRequested"/>, while saving the
/// password is handled here (it only touches this panel's flyout and the recent store).
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Controls;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    using Windows.UI.Text;
    inject bool Connecting;
    inject bool ShowConnectForm;
    inject string Url;
    inject string ServerPassword;
    inject bool UseGeneratedPassword;
    inject string CustomPassword;
    inject string ConfirmPassword;
    inject bool SaveFolderPassword;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <StackPanel Spacing=12>
            <Border Background=`theme.CardBackground` BorderBrush=`theme.CardStroke` BorderThickness=1 CornerRadius=8 Padding=`new Thickness(16, 14, 16, 14)`>
                <StackPanel Spacing=10>
                    <TextBlock Text="Start a session" FontSize=14 FontWeight=`FontWeights.SemiBold` />
                    <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Left @Click+=`await OnOpenFolder()` IsEnabled=`!Connecting` ToolTipService.ToolTip="Pick a folder to run opencode serve in; it starts with the security settings below">
                        <StackPanel Orientation=Horizontal Spacing=8>
                            <AppSymbolIcon Symbol=Folder FontSize=14 />
                            <TextBlock Text="Open Folder" VerticalAlignment=Center />
                        </StackPanel>
                    </Button>
                    <Button HorizontalAlignment=Stretch HorizontalContentAlignment=Left @Click+=`ShowConnectForm = !ShowConnectForm` IsEnabled=`!Connecting` ToolTipService.ToolTip="Connect to an existing opencode server">
                        <StackPanel Orientation=Horizontal Spacing=8>
                            <AppSymbolIcon Symbol=Globe FontSize=14 />
                            <TextBlock Text="Connect to URL" VerticalAlignment=Center />
                        </StackPanel>
                    </Button>
                    if (`ShowConnectForm`)
                    {
                        <StackPanel Spacing=8>
                            <TextBox Text<=>`Url` PlaceholderText="http://localhost:4096" IsEnabled=`!Connecting` />
                            <PasswordBox Password<=>`ServerPassword` PlaceholderText="Server password (optional)" IsEnabled=`!Connecting` />
                            <TextBlock Text="Only connect to OpenCode server that you trust" FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                            <Button Content="Connect" @Click+=`await OnConnectToUrl()` IsEnabled=`!Connecting` HorizontalAlignment=Right />
                            <TextBlock Text="Leave blank if the server has no password. Uses the OPENCODE_SERVER_PASSWORD environment variable when set. Passwords are never stored — reopening a password-protected server asks for it again." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                        </StackPanel>
                    }
                    <Border BorderBrush=`theme.DividerStroke` BorderThickness=`new Thickness(0, 1, 0, 0)` Margin=`new Thickness(0, 6, 0, 0)` />
                    <TextBlock Text="Folder security" FontSize=13 FontWeight=`FontWeights.SemiBold` />
                    <TextBlock Text="Used when you open any folder — from this list or with Open Folder." FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap />
                    <ToggleSwitch Header="Server security" OnContent="Use a generated strong password" OffContent="Set my own password" IsOn<=>`UseGeneratedPassword` IsEnabled=`!Connecting` />
                    if (`!UseGeneratedPassword`)
                    {
                        <PasswordBox Password<=>`CustomPassword` PlaceholderText="Set a password" IsEnabled=`!Connecting` />
                        <PasswordBox Password<=>`ConfirmPassword` PlaceholderText="Confirm password" IsEnabled=`!Connecting` />
                        <WrapPanel>
                            <TextBlock Text=`SaveFolderPassword ? "Password saved on this device." : "Save this password on this device?"` FontSize=11 Foreground=`theme.TertiaryText` TextWrapping=Wrap VerticalAlignment=Center Margin=`new Thickness(0, 0, 8, 0)` />
                            <Button Content=`SaveFolderPassword ? "Forget" : "Save"` FontSize=11 Padding=`new Thickness(8, 4, 8, 4)` VerticalAlignment=Center IsEnabled=`!Connecting` Flyout=passwordFlyout = <Flyout Placement=BottomEdgeAlignedRight>
                                if (`SaveFolderPassword`)
                                {
                                    <StackPanel Spacing=8 MaxWidth=260 Padding=4>
                                        <TextBlock Text="Stop saving this password?" FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap />
                                        <TextBlock Text="The password will no longer be stored on this device. You will type it again when you open a folder." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                                        <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Right>
                                            <TextBlock Text="Click outside to cancel" FontSize=11 FontStyle=`FontStyle.Italic` Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                                            <Button Content="Forget it" CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` @Click+=`SetSavePassword(false)` />
                                        </StackPanel>
                                    </StackPanel>
                                }
                                else
                                {
                                    <StackPanel Spacing=8 MaxWidth=280 Padding=4>
                                        <TextBlock Text="Store this password in plain text?" FontSize=13 FontWeight=`FontWeights.SemiBold` TextWrapping=Wrap />
                                        <TextBlock Text="If saved, the password is stored unencrypted on this computer and could be read by anyone with access to your files. Only save it if you understand this risk." FontSize=11 Foreground=`theme.SecondaryText` TextWrapping=Wrap />
                                        <StackPanel Orientation=Horizontal Spacing=8 HorizontalAlignment=Right>
                                            <TextBlock Text="Click outside to cancel" FontSize=11 FontStyle=`FontStyle.Italic` Foreground=`theme.TertiaryText` VerticalAlignment=Center />
                                            <Button Content="I understand the risk" CornerRadius=6 Padding=`new Thickness(10,  4, 10,  4)` @Click+=`SetSavePassword(true)` />
                                        </StackPanel>
                                    </StackPanel>
                                }
                            </Flyout> />
                        </WrapPanel>
                    }
                </StackPanel>
            </Border>
        </StackPanel>
    </root>
    """)]
public partial class ConnectPanel : IQuickMarkupComponent<StackPanel>
{
    /// <summary>Handler for <see cref="OpenFolderRequested"/> and <see cref="ConnectToUrlRequested"/>.</summary>
    public delegate Task ActionHandler();

    /// <summary>Raised when the user clicks Open Folder; the page picks a folder and starts serve.</summary>
    public event ActionHandler? OpenFolderRequested;

    /// <summary>Raised when the user clicks Connect in the URL form; the page connects and records the server.</summary>
    public event ActionHandler? ConnectToUrlRequested;

    [QuickMarkupConstructor]
    private void Ctor()
    {
        Init();
    }

    private async Task OnOpenFolder()
    {
        if (OpenFolderRequested is not null) await OpenFolderRequested();
    }

    private async Task OnConnectToUrl()
    {
        if (ConnectToUrlRequested is not null) await ConnectToUrlRequested();
    }

    /// <summary>
    /// Opt-in/out of persisting the custom folder password. Enabling requires confirming the
    /// plain-text-storage risk in the flyout; the choice is saved immediately either way.
    /// </summary>
    private void SetSavePassword(bool save)
    {
        if (passwordFlyout is { IsOpen: true }) passwordFlyout.Hide();
        SaveFolderPassword = save;
        RecentConnectionsStore.SaveSecurity(UseGeneratedPassword, save, CustomPassword);
    }
}
