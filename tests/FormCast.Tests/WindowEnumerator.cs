// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FormCast.Tests
{
 /// <summary>
 /// User32 P/Invoke helpers for the forced-shutdown contract
 /// validation. Test code uses <see cref="HandlesForCurrentProcess"/>
 /// to snapshot the HWNDs owned by the test runner before and after
 /// realizing FormCast forms, and <see cref="IsWindow"/> to verify
 /// that the captured HWNDs no longer exist after Plugin.Shutdown.
 /// </summary>
 internal static class WindowEnumerator
 {
 private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

 [DllImport("user32.dll")]
 [return: MarshalAs(UnmanagedType.Bool)]
 private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

 [DllImport("user32.dll")]
 private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

 [DllImport("user32.dll")]
 [return: MarshalAs(UnmanagedType.Bool)]
 public static extern bool IsWindow(IntPtr hWnd);

 /// <summary>
 /// Returns every top-level HWND owned by the current process.
 /// Includes both visible and hidden windows; the FormCast
 /// forced-shutdown contract requires that hidden Forms be
 /// destroyed too, since their HWNDs hold delegate references
 /// into the unloaded assembly.
 /// </summary>
 public static IReadOnlyList<IntPtr> HandlesForCurrentProcess()
 {
 uint myPid = (uint)Process.GetCurrentProcess().Id;
 var list = new List<IntPtr>();
 EnumWindows(
 (hwnd, _) =>
 {
 GetWindowThreadProcessId(hwnd, out uint owner);
 if (owner == myPid)
 {
 list.Add(hwnd);
 }
 return true; // keep enumerating
 },
 IntPtr.Zero);
 return list;
 }
 }
}
