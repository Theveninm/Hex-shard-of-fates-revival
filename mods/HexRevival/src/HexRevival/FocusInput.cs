using UnityEngine;

namespace HexRevival
{
    /// <summary>
    /// In a window, the game ignores the input it gets while it is not the active window: Windows sends the
    /// mouse wheel to the window under the cursor, so the map zoomed while another application was in use.
    /// The game's UI (NGUI's UICamera) reads the wheel and the keys through replaceable functions; they are
    /// wrapped here to read nothing while the window has no focus. Clicks are left alone: the click that
    /// activates the window is the one that gives it the focus.
    /// </summary>
    public static class FocusInput
    {
        public static void Install()
        {
            UICamera.GetAxisFunc axis = UICamera.GetAxis;
            UICamera.GetAxis = name => Application.isFocused ? axis(name) : 0f;
            // Key releases stay read, so that a key held when the focus is lost is not left pressed.
            UICamera.GetKeyStateFunc down = UICamera.GetKeyDown, held = UICamera.GetKey;
            UICamera.GetKeyDown = key => Application.isFocused && down(key);
            UICamera.GetKey = key => Application.isFocused && held(key);
            UICamera.GetAnyKeyFunc any = UICamera.GetAnyKeyDown;
            UICamera.GetAnyKeyDown = () => Application.isFocused && (any != null ? any() : Input.anyKeyDown);
        }
    }
}
