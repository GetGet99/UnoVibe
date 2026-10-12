namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject SessionsStateProvider Sessions;
    inject HomeProvider Home;
    inject ToastsProvider Toasts;
    inject bool IsSidebarView;
    required string Directory;
    bool ShowFileManager = false;
    bool ShowNewSession = true;
    <StackPanel Orientation=Horizontal Spacing=4>
        <SmallButtonIcon Symbol=`Symbol.Code` Tooltip="Open folder in editor" @Click+=`OnOpenInEditor()` />
        if (`ShowFileManager`)
        {
            <SmallButtonIcon Symbol=OpenLocal Tooltip="Open folder in file manager" @Click+=`OnOpenInFileManager()` />
        }
        <SmallButtonIcon Symbol=`Symbol.Terminal` Tooltip="Open folder in terminal" @Click+=`OnOpenInTerminal()` />
        if (`ShowNewSession`)
        {
            <SmallButtonIcon Symbol=Add Tooltip="New session" @Click+=`OnNewSession()` />
        }
    </StackPanel>
    """)]
partial class FolderActions : IQuickMarkupComponent
{
    private void OnOpenInEditor() => RunFolderAction(FolderLauncherHelper.OpenInEditor);

    private void OnOpenInFileManager() => RunFolderAction(FolderLauncherHelper.OpenInFileManager);

    private void OnOpenInTerminal() => RunFolderAction(FolderLauncherHelper.OpenInTerminal);

    private void OnNewSession()
    {
        Home.IsHomeView = false;
        IsSidebarView = false;
        Sessions.PrepareNewSession(Directory);
    }

    private void RunFolderAction(Func<string, string?> action)
    {
        var error = action(Directory);
        if (error is null) return;
        Toasts.ShowError(error, "Open folder");
    }
}