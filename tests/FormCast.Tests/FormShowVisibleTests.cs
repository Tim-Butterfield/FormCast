// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// tests for the visible (non-headless) <c>@FORMSHOW</c> path.
 /// Unlike <see cref="FormShowTests"/> these tests run with
 /// <c>FORMCAST_HEADLESS</c> NOT set, so <c>f_FORMSHOW</c> takes the
 /// real <c>Form.Show()</c> branch added in and the form
 /// actually appears on screen for a few hundred milliseconds before
 /// the test driver tears it down via <c>f_FORMCLOSE</c>.
 ///
 /// The five-sequential-cycle test is the load-bearing acceptance
 /// criterion from the v1 roadmap ( "5 sequential 500ms shows
 /// must succeed"). It validates that the dedicated-STA strategy
 /// confirmed in holds up across many show/close cycles
 /// through one plugin instance.
 /// </summary>
 public class FormShowVisibleTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormShowVisibleTests()
 {
 // Force headless OFF for the duration of this class so
 // f_FORMSHOW takes the real Show() branch.
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 Assert.False(global::FormCast.HeadlessMode.IsEnabled,
 "FORMCAST_HEADLESS must be unset for FormShowVisibleTests; check test parallelism.");
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize(); // ignore return value, see FormShowTests doc
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 // Restore headless ON for any test class that runs after
 // us in this assembly's serialized run order.
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "test", int w = 200, int h = 100)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle)
 {
 string[] parts = handle.Split(':');
 return int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture);
 }

 // -----------------------------------------------------------------
 // Single visible show
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSHOW_non_headless_actually_makes_form_visible()
 {
 string h = OpenForm("vis", 240, 120);
 int seq = SeqOf(h);
 try
 {
 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("0", args.ToString());
 Assert.True(_plugin.IsRealized(seq));

 // Poll FORMSTATE for the visible bit (1 = visible per
 // PLUGIN_DESIGN.md section 4.1) for up to 2 seconds.
 bool sawVisible = false;
 Stopwatch sw = Stopwatch.StartNew();
 while (sw.Elapsed < TimeSpan.FromSeconds(2))
 {
 var stateArgs = Buf(h);
 _plugin.f_FORMSTATE(stateArgs);
 if (int.TryParse(stateArgs.ToString(), out int state) && (state & 1) != 0)
 {
 sawVisible = true;
 break;
 }
 Thread.Sleep(20);
 }
 Assert.True(sawVisible,
 "FORMSTATE never reported the visible bit set after FORMSHOW " +
 "in non-headless mode.");
 }
 finally
 {
 _plugin.f_FORMCLOSE(Buf(h));
 }
 }

 // -----------------------------------------------------------------
 // The load-bearing acceptance test
 // -----------------------------------------------------------------

 [Fact]
 public void Five_sequential_FORMSHOW_FORMCLOSE_cycles_all_succeed()
 {
 for (int i = 0; i < 5; i++)
 {
 string label = "cycle" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
 string h = OpenForm(label, 180, 80);
 int seq = SeqOf(h);

 var showArgs = Buf(h);
 _plugin.f_FORMSHOW(showArgs);
 Assert.Equal("0", showArgs.ToString());
 Assert.True(_plugin.IsRealized(seq),
 "Cycle " + i + ": form not realized after FORMSHOW.");

 // Dwell for 500 ms with the form actually on screen,
 // matching the acceptance criterion verbatim.
 Thread.Sleep(500);

 var closeArgs = Buf(h);
 _plugin.f_FORMCLOSE(closeArgs);
 Assert.Equal("0", closeArgs.ToString());
 Assert.False(_plugin.IsRealized(seq),
 "Cycle " + i + ": realized form survived FORMCLOSE.");
 }

 // After five cycles the realized-form map should be empty
 // and the GUI host still running.
 Assert.Equal(0, _plugin.RealizedFormCount);
 }

 // -----------------------------------------------------------------
 // Idempotent show: calling FORMSHOW twice on the same handle is
 // a no-op the second time (form is already shown).
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSHOW_idempotent_in_non_headless_mode()
 {
 string h = OpenForm("idem");
 try
 {
 _plugin.f_FORMSHOW(Buf(h));
 Assert.Equal(1, _plugin.RealizedFormCount);

 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("0", args.ToString());
 Assert.Equal(1, _plugin.RealizedFormCount);
 }
 finally
 {
 _plugin.f_FORMCLOSE(Buf(h));
 }
 }

 // -----------------------------------------------------------------
 // Three forms shown simultaneously, then closed in arbitrary order.
 // -----------------------------------------------------------------

 [Fact]
 public void Three_simultaneous_visible_forms_each_close_independently()
 {
 string a = OpenForm("multi_a", 160, 80);
 string b = OpenForm("multi_b", 160, 80);
 string c = OpenForm("multi_c", 160, 80);

 try
 {
 _plugin.f_FORMSHOW(Buf(a));
 _plugin.f_FORMSHOW(Buf(b));
 _plugin.f_FORMSHOW(Buf(c));
 Assert.Equal(3, _plugin.RealizedFormCount);

 // Brief dwell so all three are visible together.
 Thread.Sleep(300);

 // Close in non-creation order.
 _plugin.f_FORMCLOSE(Buf(b));
 Assert.Equal(2, _plugin.RealizedFormCount);
 _plugin.f_FORMCLOSE(Buf(a));
 Assert.Equal(1, _plugin.RealizedFormCount);
 _plugin.f_FORMCLOSE(Buf(c));
 Assert.Equal(0, _plugin.RealizedFormCount);
 }
 catch
 {
 // Best-effort teardown if an assertion above fails.
 try { _plugin.f_FORMCLOSE(Buf(a)); } catch { }
 try { _plugin.f_FORMCLOSE(Buf(b)); } catch { }
 try { _plugin.f_FORMCLOSE(Buf(c)); } catch { }
 throw;
 }
 }
 }
}
