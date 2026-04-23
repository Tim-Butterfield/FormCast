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
 /// tests for the modal <c>@FORMSHOW[h, modal[:N]]</c> path
 /// in non-headless mode. The dispatch routes through
 /// <c>Form.ShowDialog</c> on the GuiHostThread with a one-shot
 /// <see cref="System.Windows.Forms.Timer"/> auto-close that fires
 /// inside the nested message loop and unblocks the dialog without
 /// human input. The dispatch returns the <c>DialogResult</c> as
 /// the buffer value (Cancel = 2 when the timer is what closed
 /// the dialog).
 ///
 /// Headless mode takes a synthetic shortcut path that returns
 /// DialogResult.Cancel without ever calling ShowDialog (so it
 /// honors the "no window ever shown" contract); that path is
 /// covered by FormShowTests.
 /// </summary>
 public class FormShowModalTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormShowModalTests()
 {
 // Force headless OFF: this class exercises the real
 // ShowDialog code path, which DOES paint a window briefly
 // before the timer-driven Close fires.
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 Assert.False(global::FormCast.HeadlessMode.IsEnabled,
 "FORMCAST_HEADLESS must be unset for FormShowModalTests.");
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 // Restore headless ON for any test class that runs after us.
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "modal", int w = 200, int h = 100)
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
 // Real ShowDialog with explicit auto-close timeout returns
 // DialogResult.Cancel (= 2).
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSHOW_modal_with_auto_close_returns_dialogresult_cancel()
 {
 string h = OpenForm("ac1");
 var args = Buf($"{h},modal:200");
 Stopwatch sw = Stopwatch.StartNew();
 _plugin.f_FORMSHOW(args);
 sw.Stop();
 Assert.Equal("2", args.ToString());
 Assert.True(sw.ElapsedMilliseconds >= 150,
 "FORMSHOW modal:200 returned in " + sw.ElapsedMilliseconds +
 " ms; the auto-close timer fired earlier than expected.");
 Assert.True(sw.ElapsedMilliseconds < 5000,
 "FORMSHOW modal:200 took " + sw.ElapsedMilliseconds +
 " ms; the auto-close timer may not be firing inside ShowDialog.");
 }

 // -----------------------------------------------------------------
 // Repeated modal calls on the same handle.
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSHOW_modal_can_be_called_repeatedly_on_same_handle()
 {
 string h = OpenForm("repeat");
 for (int i = 0; i < 3; i++)
 {
 var args = Buf($"{h},modal:150");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("2", args.ToString());
 }
 // The form is hidden but still alive in the realized map
 // for a future re-show.
 Assert.True(_plugin.IsRealized(SeqOf(h)));
 }

 // -----------------------------------------------------------------
 // FORMSTATE reports the modal bit (8) WHILE ShowDialog is
 // running. Probe from a worker thread that sleeps briefly
 // (so f_FORMSHOW has entered ShowDialog) then queries
 // FORMSTATE. The query lands inside the nested message loop
 // and observes Form.Modal == true.
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSTATE_reports_modal_bit_during_ShowDialog()
 {
 string h = OpenForm("modalstate");
 int observedBits = 0;

 var pollerDone = new ManualResetEventSlim(false);
 var poller = new Thread(() =>
 {
 try
 {
 Thread.Sleep(120); // let f_FORMSHOW enter ShowDialog
 var stateArgs = Buf(h);
 _plugin.f_FORMSTATE(stateArgs);
 int.TryParse(stateArgs.ToString(), out observedBits);
 }
 finally
 {
 pollerDone.Set();
 }
 }) { IsBackground = true };
 poller.Start();

 var showArgs = Buf($"{h},modal:500");
 _plugin.f_FORMSHOW(showArgs);
 Assert.Equal("2", showArgs.ToString());

 Assert.True(pollerDone.Wait(5000), "Poller did not finish.");
 Assert.True((observedBits & 8) != 0,
 "FORMSTATE did not report modal bit (8) during ShowDialog. observedBits=" +
 observedBits);
 }

 // -----------------------------------------------------------------
 // Validation: bad handle still returns 20100, not a DialogResult.
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSHOW_modal_unknown_handle_returns_invalid_handle()
 {
 var args = Buf("L:0:99999,modal:100");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20100", args.ToString());
 }

 // -----------------------------------------------------------------
 // Visible mode regression check: while we're running in
 // non-headless mode, sanity-check that the visible
 // path still works alongside the modal path in the
 // same plugin instance.
 // -----------------------------------------------------------------

 [Fact]
 public void Visible_show_then_modal_show_in_same_plugin_instance()
 {
 string hVis = OpenForm("vis", 180, 80);
 string hMod = OpenForm("mod", 180, 80);
 try
 {
 _plugin.f_FORMSHOW(Buf(hVis));
 Assert.True(_plugin.IsRealized(SeqOf(hVis)));

 var modalArgs = Buf($"{hMod},modal:200");
 _plugin.f_FORMSHOW(modalArgs);
 Assert.Equal("2", modalArgs.ToString());
 }
 finally
 {
 _plugin.f_FORMCLOSE(Buf(hVis));
 _plugin.f_FORMCLOSE(Buf(hMod));
 }
 }
 }
}
