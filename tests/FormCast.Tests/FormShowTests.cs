// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Text;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>@FORMSHOW</c> in (headless: realize but don't
 /// display). Most tests need a real GUI host running so the
 /// realizer can build forms; the test fixture calls
 /// <see cref="FormCast.Plugin.Initialize"/> to spin up the worker
 /// and GUI host. <c>Initialize</c> may return <c>false</c> in the
 /// xUnit context because <c>WriteMarker</c> calls into TakeCmd.dll
 /// which is not available outside a real TCC host process; the
 /// worker and GUI host are started BEFORE that point in the
 /// initialize sequence, so they are running by the time the
 /// constructor returns and that's all the tests care about.
 /// </summary>
 public class FormShowTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormShowTests()
 {
 // Force headless on for the duration of the test class.
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize(); // ignore return value -- see class doc
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 /// <summary>Open a form via f_FORMOPEN and return the
 /// full handle string (L:pid:seq).</summary>
 private string OpenForm(string name = "test", int w = 200, int h = 100)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 /// <summary>Extract the seq portion from a L:pid:seq handle string.</summary>
 private static int SeqOf(string handle)
 {
 string[] parts = handle.Split(':');
 return int.Parse(parts[2]);
 }

 // ---- Validation paths ----

 [Fact]
 public void FORMSHOW_empty_args_returns_bad_args()
 {
 var args = Buf(string.Empty);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMSHOW_whitespace_only_returns_bad_args()
 {
 var args = Buf(" ");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMSHOW_too_many_args_returns_bad_args()
 {
 var args = Buf("L:0:1,modal,extra");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMSHOW_unparseable_handle_returns_invalid_handle()
 {
 var args = Buf("notahandle");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMSHOW_unknown_handle_returns_invalid_handle()
 {
 // Properly-formatted handle that nothing has allocated.
 var args = Buf("L:0:99999");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("20100", args.ToString());
 }

 // ---- Happy path: lazy realization ----

 [Fact]
 public void FORMSHOW_valid_handle_returns_zero_and_realizes()
 {
 string h = OpenForm("alpha", 300, 200);
 int seq = SeqOf(h);
 Assert.False(_plugin.IsRealized(seq));

 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("0", args.ToString());
 Assert.True(_plugin.IsRealized(seq));
 }

 [Fact]
 public void FORMSHOW_with_modal_arg_returns_dialogresult_cancel()
 {
 // headless modal runs ShowDialog with a 100ms
 // WinForms.Timer auto-close that fires inside the
 // nested message loop and calls form.Close(); ShowDialog
 // unblocks with DialogResult.Cancel = 2.
 string h = OpenForm("beta");
 var args = Buf($"{h},modal");
 _plugin.f_FORMSHOW(args);
 Assert.Equal("2", args.ToString());
 // The form is hidden but not disposed by ShowDialog, so
 // it stays in the realized map for a possible re-show.
 Assert.True(_plugin.IsRealized(SeqOf(h)));
 }

 [Fact]
 public void FORMSHOW_idempotent_does_not_re_realize()
 {
 string h = OpenForm("gamma");
 _plugin.f_FORMSHOW(Buf(h));
 Assert.Equal(1, _plugin.RealizedFormCount);

 // Second call: still 0, still 1 form realized.
 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("0", args.ToString());
 Assert.Equal(1, _plugin.RealizedFormCount);
 }

 [Fact]
 public void FORMSHOW_does_not_actually_show_in_headless_mode()
 {
 Assert.True(global::FormCast.HeadlessMode.IsEnabled);
 string h = OpenForm("hidden");
 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 // The realizer constructs forms with Visible = false and
 // headless code path never flips that bit. The
 // proof here is that dispatch returns success and the
 // form is registered in the realized-form map; the
 // detailed Visible-stays-false assertion is covered in
 // FormRealizerTests.
 Assert.Equal("0", args.ToString());
 Assert.Equal(1, _plugin.RealizedFormCount);
 }

 [Fact]
 public void FORMSHOW_realizes_form_with_controls()
 {
 string h = OpenForm("withctrl", 400, 300);
 // Add a couple of controls via @FORMADD before showing.
 var addArgs = Buf($"{h},lbl,LABEL,5,5,100,20,Hello");
 _plugin.f_FORMADD(addArgs);
 Assert.Equal("0", addArgs.ToString());

 addArgs = Buf($"{h},btn,BUTTON,5,30,80,24,Click");
 _plugin.f_FORMADD(addArgs);
 Assert.Equal("0", addArgs.ToString());

 var args = Buf(h);
 _plugin.f_FORMSHOW(args);
 Assert.Equal("0", args.ToString());
 Assert.True(_plugin.IsRealized(SeqOf(h)));
 }

 // ---- Lifecycle: FORMCLOSE tears down realized form ----

 [Fact]
 public void FORMCLOSE_destroys_realized_form()
 {
 string h = OpenForm("disposable");
 int seq = SeqOf(h);
 _plugin.f_FORMSHOW(Buf(h));
 Assert.True(_plugin.IsRealized(seq));

 var args = Buf(h);
 _plugin.f_FORMCLOSE(args);
 Assert.Equal("0", args.ToString());
 Assert.False(_plugin.IsRealized(seq));
 Assert.Equal(0, _plugin.RealizedFormCount);
 }

 [Fact]
 public void FORMCLOSE_handle_with_no_realized_form_still_succeeds()
 {
 string h = OpenForm("never_shown");
 // Never call FORMSHOW so the handle has no realized form.
 Assert.False(_plugin.IsRealized(SeqOf(h)));

 var args = Buf(h);
 _plugin.f_FORMCLOSE(args);
 Assert.Equal("0", args.ToString());
 }

 // ---- Multiple forms ----

 [Fact]
 public void FORMSHOW_multiple_handles_each_realize_independently()
 {
 string a = OpenForm("a");
 string b = OpenForm("b");
 string c = OpenForm("c");

 _plugin.f_FORMSHOW(Buf(a));
 _plugin.f_FORMSHOW(Buf(b));
 _plugin.f_FORMSHOW(Buf(c));

 Assert.Equal(3, _plugin.RealizedFormCount);
 Assert.True(_plugin.IsRealized(SeqOf(a)));
 Assert.True(_plugin.IsRealized(SeqOf(b)));
 Assert.True(_plugin.IsRealized(SeqOf(c)));

 // Close one in the middle: the other two stay realized.
 _plugin.f_FORMCLOSE(Buf(b));
 Assert.Equal(2, _plugin.RealizedFormCount);
 Assert.True(_plugin.IsRealized(SeqOf(a)));
 Assert.False(_plugin.IsRealized(SeqOf(b)));
 Assert.True(_plugin.IsRealized(SeqOf(c)));
 }
 }
}
