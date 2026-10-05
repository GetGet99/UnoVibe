namespace UnoVibe.Controls;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject SessionsStateProvider Sessions;
    inject ToastsProvider Toasts;
    inject bool IsSidebarView;
    required string Directory;
    bool ShowFileManager = false;
    bool ShowNewSession = true;
    <StackPanel Orientation=Horizontal Spacing=4>
        <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open folder in editor" @Click+=`OnOpenInEditor()`>
            <AppSymbolIcon Symbol=`Symbol.Code` FontSize=11 />
        </Button>
        if (`ShowFileManager`)
        {
            <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open folder in file manager" @Click+=`OnOpenInFileManager()`>
                <AppSymbolIcon Symbol=OpenLocal FontSize=11 />
            </Button>
        }
        <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open folder in terminal" @Click+=`OnOpenInTerminal()`>
            <AppSymbolIcon Symbol=`Symbol.Terminal` FontSize=11 />
        </Button>
        if (`ShowNewSession`)
        {
            <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="New session" @Click+=`OnNewSession()`>
                <AppSymbolIcon Symbol=Add FontSize=11 />
            </Button>
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