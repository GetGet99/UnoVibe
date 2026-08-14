using UnoVibe.Models;
using UnoVibe.Services;

namespace UnoVibe.Controls;

/// <summary>
/// A row of small folder-action buttons for <paramref name="Directory"/>: open in VS Code,
/// open in the file manager, open in a terminal, and (optionally) start a new session.
/// All click handling lives here. Reused by the session sidebar and the chat header; the
/// file-manager button and the new-session button can be disabled per site.
/// </summary>
[QuickMarkup("""
    using UnoVibe.Services;
    using UnoVibe.Models;
    using QuickMarkup.WinUI;
    using Microsoft.UI;
    inject ChatStore Store;
    required string Directory;
    bool ShowFileManager = false;
    bool ShowNewSession = true;
    <StackPanel Orientation=Horizontal Spacing=4>
        <Button Padding=`new Thickness(6, 4, 6, 4)` ToolTipService.ToolTip="Open folder in VS Code" @Click+=`OnOpenInVSCode()`>
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
public partial class FolderActions : IQuickMarkupComponent
{
    private void OnOpenInVSCode() => RunFolderAction(FolderLauncher.OpenInVSCode);

    private void OnOpenInFileManager() => RunFolderAction(FolderLauncher.OpenInFileManager);

    private void OnOpenInTerminal() => RunFolderAction(FolderLauncher.OpenInTerminal);

    private void OnNewSession() => _ = Store.NewSessionAsync(Directory);

    /// <summary>Runs a folder-launch action and surfaces failures as a toast.</summary>
    private void RunFolderAction(Func<string, string?> action)
    {
        var error = action(Directory);
        if (error is null) return;
        Store.ShowToast(new ToastItem
        {
            Title = "Open folder",
            Message = error,
            Variant = "error",
        });
    }
}