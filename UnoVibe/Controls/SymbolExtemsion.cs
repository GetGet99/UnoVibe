namespace UnoVibe.Controls;
// More symbols on https://learn.microsoft.com/en-us/windows/apps/design/iconography/segoe-fluent-icons-font
// Note: to use these symbols in QuickMarkup you will need to wrap in backtick expression as QuickMarkup does not find discover extensions.
static class SymbolExtension {
    extension(Symbol)
    {
        public static Symbol PrivateCall => (Symbol)0xea3d;
        public static Symbol Code => (Symbol)0xe943;
        public static Symbol Terminal => (Symbol)0xe756;
        public static Symbol GlobalNavButton => (Symbol)0xe700;
        public static Symbol ChevronUp => (Symbol)0xe70e;
        public static Symbol ChevronDown => (Symbol)0xe70d;
    }
}