// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

using FormCast.Forms;
using FormCast.Threading;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// cohabitation strategy probe: validate empirically that the
 /// existing dedicated-STA <see cref="GuiHostThread"/> message loop
 /// can pump a real <c>Form.Show()</c> call followed by a one-shot
 /// <see cref="System.Windows.Forms.Timer"/>-driven dismiss without
 /// deadlock, cross-thread exceptions, or leaked windows.
 ///
 /// PLUGIN_DESIGN.md section 7 #17 lists three candidate
 /// cohabitation strategies; FormCast committed up front to the
 /// dedicated STA pattern. is the milestone that proves it
 /// works in practice. If these tests pass, (the public
 /// <c>@FORMSHOW</c> visible behavior) can be built directly on
 /// the same plumbing.
 /// </summary>
 public class FormShowAutoCloseTests : IDisposable
 {
 private readonly GuiHostThread _host;

 public FormShowAutoCloseTests()
 {
 _host = new GuiHostThread();
 _host.Start();
 }

 public void Dispose()
 {
 _host.Stop();
 _host.Dispose();
 }

 // -----------------------------------------------------------------
 // Helper: poll IsDisposed on the GUI thread until true or timeout.
 // -----------------------------------------------------------------
 private bool WaitForDisposed(Form form, TimeSpan timeout)
 {
 Stopwatch sw = Stopwatch.StartNew();
 while (sw.Elapsed < timeout)
 {
 bool disposed = false;
 _host.Invoke(() => disposed = form.IsDisposed);
 if (disposed) { return true; }
 Thread.Sleep(25);
 }
 return false;
 }

 [Fact]
 public void Single_show_auto_close_cycle_completes_within_timeout()
 {
 var desc = new FormDescriptor
 {
 Name = "probe",
 Title = "probe",
 X = 0, Y = 0, Width = 200, Height = 100,
 };
 Form form = FormRealizer.Realize(desc, _host);
 try
 {
 FormRealizer.ShowAutoClose(form, _host, autoCloseMs: 200);

 // The probe call returns immediately; the timer fires
 // on the GUI thread and closes the form. Allow up to 5
 // seconds for the close to land before declaring the
 // cohabitation strategy broken.
 bool closed = WaitForDisposed(form, TimeSpan.FromSeconds(5));
 Assert.True(closed,
 "Form was not disposed after ShowAutoClose; the dedicated-STA " +
 "cohabitation strategy may not be pumping the timer tick.");
 }
 finally
 {
 FormRealizer.Destroy(form, _host);
 }
 }

 [Fact]
 public void Show_auto_close_actually_makes_form_visible_briefly()
 {
 var desc = new FormDescriptor { Name = "v", Width = 120, Height = 80 };
 Form form = FormRealizer.Realize(desc, _host);
 try
 {
 bool visibleObserved = false;
 FormRealizer.ShowAutoClose(form, _host, autoCloseMs: 400);

 // Poll on the GUI thread for the brief window where
 // form.Visible is true. The form was created with
 // Visible = false; only ShowAutoClose's Form.Show()
 // call can set it true.
 Stopwatch sw = Stopwatch.StartNew();
 while (sw.Elapsed < TimeSpan.FromMilliseconds(800))
 {
 bool isVisible = false;
 bool isDisposed = false;
 _host.Invoke(() =>
 {
 isDisposed = form.IsDisposed;
 if (!isDisposed) { isVisible = form.Visible; }
 });
 if (isVisible) { visibleObserved = true; break; }
 if (isDisposed) { break; }
 Thread.Sleep(10);
 }

 Assert.True(visibleObserved,
 "Form.Visible never observed true; Form.Show() did not " +
 "take effect inside the dedicated-STA message loop.");

 // Wait for the auto-close to finish before tearing down.
 WaitForDisposed(form, TimeSpan.FromSeconds(5));
 }
 finally
 {
 FormRealizer.Destroy(form, _host);
 }
 }

 [Fact]
 public void Five_sequential_show_auto_close_cycles_all_complete()
 {
 // The forced-shutdown contract and the lifetime tests
 // already exercise repeated load/unload of the host. This
 // test exercises repeated SHOW/CLOSE through one host, which
 // is what will need: a single GuiHostThread serving
 // many @FORMSHOW calls across the lifetime of one plugin
 // load. If the message loop or timer infrastructure leaks
 // between cycles, this test catches it now.
 for (int i = 0; i < 5; i++)
 {
 var desc = new FormDescriptor
 {
 Name = "cycle" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
 Width = 100, Height = 60,
 };
 Form form = FormRealizer.Realize(desc, _host);
 try
 {
 FormRealizer.ShowAutoClose(form, _host, autoCloseMs: 150);
 bool closed = WaitForDisposed(form, TimeSpan.FromSeconds(5));
 Assert.True(closed,
 "Cycle " + i.ToString(System.Globalization.CultureInfo.InvariantCulture) +
 " did not close within 5 seconds.");
 }
 finally
 {
 FormRealizer.Destroy(form, _host);
 }
 }

 // After five cycles the host should still be running and
 // pumping. If the message loop died, IsRunning will be
 // false or the next Invoke will throw.
 Assert.True(_host.IsRunning, "GuiHostThread is no longer running after five show/close cycles.");
 int echoed = 0;
 _host.Invoke(() => echoed = 42);
 Assert.Equal(42, echoed);
 }

 [Fact]
 public void ShowAutoClose_validates_arguments()
 {
 Assert.Throws<ArgumentNullException>(() =>
 FormRealizer.ShowAutoClose(null!, _host, 100));

 var desc = new FormDescriptor { Name = "x", Width = 50, Height = 50 };
 Form form = FormRealizer.Realize(desc, _host);
 try
 {
 Assert.Throws<ArgumentNullException>(() =>
 FormRealizer.ShowAutoClose(form, null!, 100));
 Assert.Throws<ArgumentOutOfRangeException>(() =>
 FormRealizer.ShowAutoClose(form, _host, 0));
 Assert.Throws<ArgumentOutOfRangeException>(() =>
 FormRealizer.ShowAutoClose(form, _host, -1));
 }
 finally
 {
 FormRealizer.Destroy(form, _host);
 }
 }
 }
}
