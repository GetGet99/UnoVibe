namespace UnoVibe.Controls;

/// <summary>
/// Renders a Symbol glyph (like <see cref="SymbolIcon"/>) as a <see cref="FontIcon"/>, so the
/// icon size can be controlled via <see cref="FontSize"/> instead of the fixed 16px SymbolIcon.
/// </summary>
/// <remarks>
/// The <c>Symbol</c> type in the markup is Uno Platform's
/// <see cref="Microsoft.UI.Xaml.Controls.Symbol"/> enum (WinUI glyphs: Edit, Font, Bullets,
/// Undo, Folder, Add, ...), resolved via the global usings in
/// <c>UnoVibe/GlobalUsings.cs</c> — it is NOT defined in this project. There is no separate
/// Uno.WinUI package reference; the enum comes from the Uno.Sdk implicit packages.
/// Glyphs render as <see cref="FontIcon"/> chars because Uno's <see cref="SymbolIcon"/> is a
/// fixed 16px; list the names you use here so contributors can look them up in the Uno
/// <see cref="Microsoft.UI.Xaml.Controls.Symbol"/> enum when adding new icons.
/// </remarks>
[QuickMarkup("""
    Symbol Symbol = Edit;
    double FontSize = 16;
    <FontIcon Glyph=`((char)Symbol).ToString()` FontSize=`FontSize` />
    """)]
public partial class AppSymbolIcon : IQuickMarkupComponent<FontIcon>;