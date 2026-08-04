namespace UnoVibe.Controls;

/// <summary>
/// Renders a Symbol glyph (like <see cref="SymbolIcon"/>) as a <see cref="FontIcon"/>, so the
/// icon size can be controlled via <see cref="FontSize"/> instead of the fixed 16px SymbolIcon.
/// </summary>
[QuickMarkup("""
    Symbol Symbol = Edit;
    double FontSize = 16;
    <FontIcon Glyph=`((char)Symbol).ToString()` FontSize=`FontSize` />
    """)]
public partial class AppSymbolIcon : IQuickMarkupComponent<FontIcon>;