namespace UnoVibe.Controls;

[QuickMarkup("""
    Symbol Symbol = Edit;
    double FontSize = 16;
    <FontIcon Glyph=`((char)Symbol).ToString()` FontSize=`FontSize` />
    """)]
partial class AppSymbolIcon : IQuickMarkupComponent<FontIcon>;