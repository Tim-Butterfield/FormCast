// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// forced shutdown contract validation. Open three forms,
 /// realize them, capture the HWNDs the realizer creates on the
 /// GUI host thread, call <c>Plugin.Shutdown</c>, then verify that
 /// every captured HWND has been destroyed -- not merely hidden,
 /// not merely closed, but completely gone, so user code that
 /// somehow held a reference to the HWND would see <c>IsWindow</c>
 /// return <c>false</c>.
 ///
 /// Why this matters: a surviving FormCast HWND holds delegate
 /// references into the (now-unloaded) plugin assembly. The next
 /// click on it would dispatch into freed memory and crash TCC.
 /// PLUGIN_DESIGN.md section 4.6 calls this out as the single
 /// load-bearing invariant of the Phase 4 design.
 /// </summary>
 public class ForcedShutdownTests : IDisposable
 {
 public ForcedShutdownTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 }

 public void Dispose()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private static string OpenForm(global::FormCast.Plugin p, string name)
 {
 var args = Buf($"form,{name},10,10,200,100");
 p.f_FORMOPEN(args);
 return args.ToString();
 }

 [Fact]
 public void Open_three_forms_then_shutdown_destroys_every_HWND()
 {
 var plugin = new global::FormCast.Plugin();
 try
 {
 plugin.Initialize();

 // Snapshot HWNDs BEFORE we realize anything. The set
 // includes the GuiHostThread's marshaler control plus
 // anything the test runner already had open.
 var before = new HashSet<IntPtr>(
 WindowEnumerator.HandlesForCurrentProcess());

 // Open three forms and show each one. FORMSHOW does
 // the lazy realize, which constructs a Form on the
 // GUI thread; the HWND is created when SaveImage or
 // any other render path forces it. To make this test
 // exercise the actual forced-shutdown sweep, we use
 // FORMSAVEIMAGE on each form -- that path explicitly
 // forces handle creation.
 string h1 = OpenForm(plugin, "alpha");
 string h2 = OpenForm(plugin, "beta");
 string h3 = OpenForm(plugin, "gamma");

 string tmpDir = System.IO.Path.Combine(
 System.IO.Path.GetTempPath(),
 "FormCast.Forced." + Guid.NewGuid().ToString("N"));
 System.IO.Directory.CreateDirectory(tmpDir);
 try
 {
 plugin.f_FORMSAVEIMAGE(Buf($"{h1},{tmpDir}\\a.png"));
 plugin.f_FORMSAVEIMAGE(Buf($"{h2},{tmpDir}\\b.png"));
 plugin.f_FORMSAVEIMAGE(Buf($"{h3},{tmpDir}\\c.png"));

 Assert.Equal(3, plugin.RealizedFormCount);

 // Snapshot AFTER realization: should have at least
 // 3 new HWNDs (one per realized form). The actual
 // delta may be larger because WinForms creates
 // helper HWNDs (parking windows, etc.) that the
 // forced-shutdown sweep also tears down.
 var after = WindowEnumerator.HandlesForCurrentProcess();
 var newHandles = after.Where(h => !before.Contains(h)).ToList();
 Assert.True(newHandles.Count >= 3,
 $"Expected at least 3 new HWNDs after realizing 3 forms; saw {newHandles.Count}.");

 // The forced shutdown contract: after Shutdown
 // returns, every HWND owned by FormCast must be
 // destroyed.
 plugin.Shutdown(endProcess: false);

 // Walk the HWNDs we captured and verify each one
 // is no longer a window. Helper HWNDs (parking
 // windows, message-only) may legitimately survive
 // if WinForms recycles them at AppDomain scope --
 // we focus the assertion on the realized forms by
 // checking that ALL new handles are destroyed,
 // because the GuiHostThread teardown also disposes
 // its marshaler control.
 var survivors = newHandles
 .Where(h => WindowEnumerator.IsWindow(h))
 .ToList();
 Assert.True(survivors.Count == 0,
 $"Forced shutdown leaked {survivors.Count} HWND(s): " +
 string.Join(", ", survivors.Select(h => "0x" + h.ToInt64().ToString("X"))));

 // Belt and braces: the realized-form map is empty.
 Assert.Equal(0, plugin.RealizedFormCount);
 }
 finally
 {
 try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
 }
 }
 finally
 {
 // If Shutdown was already called above this is a no-op
 // for our purposes; the worker and gui host are both
 // idempotent on Stop.
 try { plugin.Shutdown(endProcess: false); } catch { }
 }
 }

 [Fact]
 public void Shutdown_with_zero_forms_completes_without_throwing()
 {
 var plugin = new global::FormCast.Plugin();
 plugin.Initialize();
 // No forms opened. Shutdown should still tear down the
 // gui host and callback worker cleanly.
 plugin.Shutdown(endProcess: false);
 }

 [Fact]
 public void Shutdown_called_twice_is_safe()
 {
 var plugin = new global::FormCast.Plugin();
 plugin.Initialize();
 string h = OpenForm(plugin, "doubled");
 plugin.f_FORMSHOW(Buf(h));

 plugin.Shutdown(endProcess: false);
 // Second call: must not throw, must not double-dispose.
 plugin.Shutdown(endProcess: false);
 }
 }
}
