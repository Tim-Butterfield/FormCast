// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Linq;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// add-on tests: PROGRESSBAR control type, @FORMTASKDIALOG
 /// (headless suppressed path), @FORMFOCUS, @FORMSENDMESSAGE,
 /// @FORMHITTEST. Headless mode keeps every test deterministic and
 /// fast.
 /// </summary>
 public class M12AddOnsTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public M12AddOnsTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize();
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "test", int w = 400, int h = 300)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle) =>
 int.Parse(handle.Split(':')[2], System.Globalization.CultureInfo.InvariantCulture);

 // -----------------------------------------------------------------
 // PROGRESSBAR
 // -----------------------------------------------------------------

 [Fact]
 public void PROGRESSBAR_is_a_recognized_control_type()
 {
 Assert.True(ControlBuilders.IsRecognizedType("PROGRESSBAR"));
 Assert.True(ControlBuilders.IsRecognizedType("progressbar"));
 }

 [Fact]
 public void PROGRESSBAR_FORMADD_succeeds_and_round_trips_props()
 {
 string h = OpenForm();
 var args = Buf($"{h},pb,PROGRESSBAR,10,10,200,20,");
 _plugin.f_FORMADD(args);
 Assert.Equal("0", args.ToString());

 _plugin.f_FORMSET(Buf($"{h},pb,min,0"));
 _plugin.f_FORMSET(Buf($"{h},pb,max,200"));
 _plugin.f_FORMSET(Buf($"{h},pb,value,75"));
 _plugin.f_FORMSET(Buf($"{h},pb,style,continuous"));

 var form = _plugin.LookupDescriptor(SeqOf(h))!;
 var pb = form.Controls.First(c => c.Id == "pb");
 Assert.Equal("0", pb.Properties["min"]);
 Assert.Equal("200", pb.Properties["max"]);
 Assert.Equal("75", pb.Properties["value"]);
 Assert.Equal("continuous", pb.Properties["style"]);
 }

 // -----------------------------------------------------------------
 // @FORMTASKDIALOG (headless suppressed)
 // -----------------------------------------------------------------

 [Fact]
 public void FORMTASKDIALOG_in_headless_returns_zero_without_blocking()
 {
 var args = Buf("Title,Body text,ok,info");
 int rc = _plugin.f_FORMTASKDIALOG(args);
 Assert.Equal(0, rc);
 Assert.Equal("0", args.ToString());
 }

 [Fact]
 public void FORMTASKDIALOG_rejects_too_few_args()
 {
 var args = Buf("OnlyOneArg");
 _plugin.f_FORMTASKDIALOG(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMTASKDIALOG_rejects_too_many_args()
 {
 var args = Buf("a,b,c,d,e");
 _plugin.f_FORMTASKDIALOG(args);
 Assert.Equal("20101", args.ToString());
 }

 // -----------------------------------------------------------------
 // @FORMFOCUS
 // -----------------------------------------------------------------

 [Fact]
 public void FORMFOCUS_TCC_returns_a_status_code()
 {
 // GetConsoleWindow may return IntPtr.Zero in the xUnit
 // process if it has no real console; that case maps to
 // 20100 (invalid handle). Either 0 or 20100 is acceptable
 // here -- we are testing the dispatch surface, not the
 // OS focus behavior.
 var args = Buf("TCC");
 _plugin.f_FORMFOCUS(args);
 string r = args.ToString();
 Assert.True(r == "0" || r == "1" || r == "20100",
 "FORMFOCUS[TCC] returned unexpected value: " + r);
 }

 [Fact]
 public void FORMFOCUS_empty_args_returns_bad_args()
 {
 var args = Buf("");
 _plugin.f_FORMFOCUS(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMFOCUS_garbage_handle_returns_invalid_handle()
 {
 var args = Buf("notahandle");
 _plugin.f_FORMFOCUS(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMFOCUS_unknown_handle_returns_invalid_handle()
 {
 var args = Buf("L:0:99999");
 _plugin.f_FORMFOCUS(args);
 Assert.Equal("20100", args.ToString());
 }

 // -----------------------------------------------------------------
 // @FORMSENDMESSAGE
 // -----------------------------------------------------------------

 [Fact]
 public void FORMSENDMESSAGE_rejects_wrong_arg_count()
 {
 var args = Buf("L:0:1,0,0");
 _plugin.f_FORMSENDMESSAGE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMSENDMESSAGE_unknown_handle_returns_invalid_handle()
 {
 var args = Buf("L:0:99999,0x10,0,0");
 _plugin.f_FORMSENDMESSAGE(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMSENDMESSAGE_garbage_msg_returns_bad_args()
 {
 string h = OpenForm();
 var args = Buf($"{h},notamsg,0,0");
 _plugin.f_FORMSENDMESSAGE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMSENDMESSAGE_to_unrealized_form_returns_invalid_handle()
 {
 string h = OpenForm();
 // Form is open but not realized -> not in _realizedForms.
 var args = Buf($"{h},0x10,0,0");
 _plugin.f_FORMSENDMESSAGE(args);
 Assert.Equal("20100", args.ToString());
 }

 // -----------------------------------------------------------------
 // @FORMHITTEST
 // -----------------------------------------------------------------

 [Fact]
 public void FORMHITTEST_rejects_wrong_arg_count()
 {
 var args = Buf("L:0:1,5");
 _plugin.f_FORMHITTEST(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMHITTEST_unrealized_form_returns_empty()
 {
 string h = OpenForm();
 var args = Buf($"{h},10,10");
 _plugin.f_FORMHITTEST(args);
 Assert.Equal(string.Empty, args.ToString());
 }

 [Fact]
 public void FORMHITTEST_garbage_coordinates_returns_bad_args()
 {
 string h = OpenForm();
 var args = Buf($"{h},x,y");
 _plugin.f_FORMHITTEST(args);
 Assert.Equal("20101", args.ToString());
 }
 }
}
