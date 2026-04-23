// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Interop/NativeMethods.cs
// ========================
//
// Win32 P/Invokes used by the add-on dispatch verbs
// (@FORMFOCUS, @FORMSENDMESSAGE, @FORMHITTEST). Kept narrow on
// purpose: only the exact entry points the plugin needs, no
// general-purpose Win32 wrapper.

using System;
using System.Runtime.InteropServices;

namespace FormCast.Interop
{
 /// <summary>
 /// Narrow set of Win32 P/Invokes used by the extension dispatch
 /// verbs. Internal so the public surface stays focused on the
 /// @FORM* contract.
 /// </summary>
    internal static class NativeMethods
    {
 /// <summary>
 /// kernel32!GetConsoleWindow -- HWND of the parent process's
 /// console window. Used by <c>@FORMFOCUS[TCC]</c> to focus the
 /// TCC console after a form-driven interaction.
 /// </summary>
        [DllImport("kernel32.dll", ExactSpelling = true)]
        public static extern IntPtr GetConsoleWindow();

 /// <summary>
 /// user32!SetForegroundWindow -- bring the named window to
 /// the foreground. Returns false on certain UIPI / focus-
 /// stealing-prevention scenarios; the caller surfaces that
 /// to the BTM script as a non-zero return code.
 /// </summary>
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

 /// <summary>
 /// user32!SendMessageW -- the Unicode message-send entry point.
 /// Synchronous: blocks the caller until the target window's
 /// message handler returns.
 /// </summary>
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

 /// <summary>
 /// user32!ShowWindow -- control a window's visibility state.
 /// Used by <c>@FORMCONSOLE</c> to hide/show/minimize the
 /// TCC console window.
 /// </summary>
        [DllImport("user32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

 /// <summary>SW_HIDE (0) -- hide the window.</summary>
        public const int SW_HIDE = 0;
 /// <summary>SW_SHOWNORMAL (1) -- show and restore.</summary>
        public const int SW_SHOWNORMAL = 1;
 /// <summary>SW_SHOWMINIMIZED (2) -- show minimized.</summary>
        public const int SW_SHOWMINIMIZED = 2;
 /// <summary>SW_SHOW (5) -- show in current size/position.</summary>
        public const int SW_SHOW = 5;
 /// <summary>SW_RESTORE (9) -- restore from minimized.</summary>
        public const int SW_RESTORE = 9;

 /// <summary>
 /// dwmapi!DwmSetWindowAttribute -- set Desktop Window Manager
 /// attributes on a window. Used to opt a window into immersive
 /// dark mode on Windows 10 1809+ / Windows 11.
 /// </summary>
        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(
            IntPtr hwnd, int attr, ref int attrValue, int attrSize);

 /// <summary>DWMWA_USE_IMMERSIVE_DARK_MODE (20).</summary>
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

 /// <summary>
 /// shell32!SetCurrentProcessExplicitAppUserModelID -- set the
 /// AppUserModelID for the current process. This controls how
 /// Windows groups windows in the taskbar. Setting a custom ID
 /// prevents FormCast windows from being grouped with TCC's
 /// console window and allows them to show their own icon.
 /// </summary>
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        public static extern void SetCurrentProcessExplicitAppUserModelID(
            [MarshalAs(UnmanagedType.LPWStr)] string appId);

 /// <summary>WM_SETICON (0x0080) -- set small or large icon.</summary>
        public const uint WM_SETICON = 0x0080;
 /// <summary>ICON_SMALL (0) -- 16x16 title bar icon.</summary>
        public static readonly IntPtr ICON_SMALL = IntPtr.Zero;
 /// <summary>ICON_BIG (1) -- 32x32 ALT+TAB / taskbar icon.</summary>
        public static readonly IntPtr ICON_BIG = new IntPtr(1);

 /// <summary>
 /// Explicitly send WM_SETICON to a window. This forces the
 /// taskbar to update its cached icon for the window, which
 /// Form.Icon alone does not always accomplish when the process
 /// is a console app (like TCC) whose process icon differs.
 /// </summary>
        public static void ForceWindowIcon(IntPtr hwnd, System.Drawing.Icon icon)
        {
            if (hwnd == IntPtr.Zero || icon is null) { return; }
            SendMessage(hwnd, WM_SETICON, ICON_BIG, icon.Handle);
            SendMessage(hwnd, WM_SETICON, ICON_SMALL, icon.Handle);
        }
    }
}
