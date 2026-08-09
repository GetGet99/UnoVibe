namespace UnoVibe.Controls;

static class SymbolExtension {
    // More symbols on https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font
    // Note: to use these symbols in QuickMarkup you will need to wrap in backtick expression as QuickMarkup does not find discover extensions.
    extension(Symbol)
    {
        public static Symbol PrivateCall => (Symbol)0xea3d;
        // Code icon `{ }` used for "open in editor" actions.
        public static Symbol Code => (Symbol)0xe943;
    }
}