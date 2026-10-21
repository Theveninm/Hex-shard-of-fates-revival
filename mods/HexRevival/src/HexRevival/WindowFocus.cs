using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace HexRevival
{
    /// <summary>
    /// Whether the game's window is the active window, asked to Windows. Unity's Application.isFocused is not
    /// reliable here: it stayed true while another window was in front (checked in game), and could be wrong
    /// the other way. Used to ignore the mouse wheel Windows sends to the window under the cursor.
    /// </summary>
    public static class WindowFocus
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static uint s_Pid;
        private static bool s_Unavailable;

        /// <summary>True when the active window belongs to the game (true if Windows cannot be asked).</summary>
        public static bool IsActive()
        {
            if (s_Unavailable) return true;
            try
            {
                if (s_Pid == 0) s_Pid = (uint)Process.GetCurrentProcess().Id;
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                return pid == s_Pid;
            }
            catch (Exception) { s_Unavailable = true; return true; }
        }
    }
}
