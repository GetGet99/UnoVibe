using Microsoft.UI.Input;
using Windows.UI.Core;

namespace UnoVibe.Helpers;

static class KeyboardExtension
{
    extension(InputKeyboardSource)
    {
        public static CoreVirtualKeyStates GetCModifierKeyStateForCurrentThread()
        {
#if DESKTOP_MACOS
            var a = InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.LeftWindows
            );
            if (a is not CoreVirtualKeyStates.None) return a;
            return InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.RightWindows
            );
#else
            return InputKeyboardSource.GetKeyStateForCurrentThread(
                Windows.System.VirtualKey.Control
            );
#endif
        }
    }
}
