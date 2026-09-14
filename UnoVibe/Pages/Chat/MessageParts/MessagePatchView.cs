namespace UnoVibe.Pages.Chat.MessageParts;

[QuickMarkup("""
    using QuickMarkup.WinUI;
    required PatchPartItem Part;
    <setup>
        var theme = ThemeBrushes.Global;
    </setup>
    <root>
        <Border Background=`theme.SubtleFill` CornerRadius=4 Padding=`new Thickness(8, 4, 8, 4)`>
            <TextBlock Text=`Part.Files.Count > 0 ? $"Edited {Part.Files.Count} file(s): " + string.Join(", ", Part.Files) : "file changes"` FontSize=12 TextWrapping=Wrap IsTextSelectionEnabled=true />
        </Border>
    </root>
    """)]
partial class MessagePatchView : IQuickMarkupComponent;
