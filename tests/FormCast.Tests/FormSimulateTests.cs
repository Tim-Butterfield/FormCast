// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Text;
using System.Windows.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>@FORMSIMULATE</c> in synthetic event dispatch
 /// via the dispatch surface plus the underlying
 /// <c>FormRealizer.Simulate</c> helper. The dispatch path is
 /// validated for argument shapes, error codes, and lazy
 /// realization; the WinForms-state path is validated by
 /// inspecting the realized form's controls (marshaled back to the
 /// GUI thread because cross-thread access throws otherwise).
 /// </summary>
 public class FormSimulateTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;

 public FormSimulateTests()
 {
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", "1");
 global::FormCast.HeadlessMode.Refresh();
 _plugin = new global::FormCast.Plugin();
 _plugin.Initialize(); // worker + gui host start before WriteMarker fault
 }

 public void Dispose()
 {
 _plugin.Shutdown(endProcess: false);
 Environment.SetEnvironmentVariable("FORMCAST_HEADLESS", null);
 global::FormCast.HeadlessMode.Refresh();
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "test", int w = 300, int h = 200)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private static int SeqOf(string handle)
 {
 string[] parts = handle.Split(':');
 return int.Parse(parts[2]);
 }

 /// <summary>Add a control to a form via the dispatch surface.</summary>
 private void AddControl(string handle, string id, string type,
 int x = 5, int y = 5, int w = 100, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 /// <summary>Marshal a read of a control property onto the GUI thread.</summary>
 private T ReadOnGui<T>(int seq, string controlId, Func<Control, T> reader)
 {
 Form? form = _plugin.TryGetRealizedForm(seq);
 Assert.NotNull(form);
 T result = default!;
 _plugin.GuiHost.Invoke(() =>
 {
 Control? c = FindControl(form!, controlId);
 Assert.NotNull(c);
 result = reader(c!);
 });
 return result;
 }

 private static Control? FindControl(Control parent, string name)
 {
 foreach (Control c in parent.Controls)
 {
 if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
 {
 return c;
 }
 Control? nested = FindControl(c, name);
 if (nested is not null) { return nested; }
 }
 return null;
 }

 // ---------- Validation paths ----------

 [Fact]
 public void Empty_args_returns_bad_args()
 {
 var args = Buf(string.Empty);
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void Two_args_returns_bad_args()
 {
 var args = Buf("L:0:1,btn");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void Five_args_returns_bad_args()
 {
 var args = Buf("L:0:1,btn,click,extra,extra2");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void Empty_action_returns_bad_args()
 {
 var args = Buf("L:0:1,btn,");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void Unparseable_handle_returns_invalid_handle()
 {
 var args = Buf("notahandle,btn,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void Unknown_handle_returns_invalid_handle()
 {
 var args = Buf("L:0:99999,btn,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void Unknown_control_id_returns_unknown_control()
 {
 string h = OpenForm("formA");
 AddControl(h, "btnReal", "BUTTON");

 var args = Buf($"{h},btnGhost,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20103", args.ToString());
 }

 [Fact]
 public void Unknown_action_returns_unknown_action()
 {
 string h = OpenForm("formB");
 AddControl(h, "btn1", "BUTTON");

 var args = Buf($"{h},btn1,wiggle");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20107", args.ToString());
 }

 [Fact]
 public void Type_action_on_button_returns_unknown_action_code()
 {
 // Action is recognized, but does not apply to a Button.
 // Per design, both unknown-action and unsupported-for-
 // control share the 20107 result code.
 string h = OpenForm("formC");
 AddControl(h, "btn1", "BUTTON");

 var args = Buf($"{h},btn1,type,hello");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("20107", args.ToString());
 }

 // ---------- Happy paths ----------

 [Fact]
 public void Click_on_button_fires_click_handler()
 {
 string h = OpenForm("clickform");
 AddControl(h, "go", "BUTTON", text: "Go");
 int seq = SeqOf(h);

 // Realize and wire a Click handler that flips a flag. We
 // wire the handler on the GUI thread because adding a
 // delegate to a Control event is a thread-affine operation.
 _plugin.f_FORMSHOW(Buf(h)); // lazy-realize
 int clickCount = 0;
 _plugin.GuiHost.Invoke(() =>
 {
 Control c = FindControl(_plugin.TryGetRealizedForm(seq)!, "go")!;
 ((Button)c).Click += (s, e) => clickCount++;
 });

 var args = Buf($"{h},go,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());
 Assert.Equal(1, clickCount);
 }

 [Fact]
 public void Type_on_textbox_appends_text()
 {
 // Note: ArgParser.Split currently trims whitespace from each
 // comma-delimited token, so we cannot pass a value with a
 // leading or trailing space through the dispatch surface
 // until followup quoted-comma support lands. The
 // append semantics still get exercised by concatenating two
 // unspaced tokens.
 string h = OpenForm("typeform");
 AddControl(h, "name", "EDIT", text: "abc");
 int seq = SeqOf(h);

 var args = Buf($"{h},name,type,def");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());

 string text = ReadOnGui(seq, "name", c => c.Text);
 Assert.Equal("abcdef", text);
 }

 [Fact]
 public void Settext_on_label_replaces_text()
 {
 string h = OpenForm("setform");
 AddControl(h, "lbl", "LABEL", text: "before");
 int seq = SeqOf(h);

 var args = Buf($"{h},lbl,settext,after");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());

 string text = ReadOnGui(seq, "lbl", c => c.Text);
 Assert.Equal("after", text);
 }

 [Fact]
 public void Check_on_checkbox_sets_checked_true()
 {
 string h = OpenForm("checkform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 var args = Buf($"{h},agree,check");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());

 bool isChecked = ReadOnGui(seq, "agree", c => ((CheckBox)c).Checked);
 Assert.True(isChecked);
 }

 [Fact]
 public void Uncheck_on_checkbox_sets_checked_false()
 {
 string h = OpenForm("uncheckform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 // First check it via dispatch, then uncheck it.
 _plugin.f_FORMSIMULATE(Buf($"{h},agree,check"));
 Assert.True(ReadOnGui(seq, "agree", c => ((CheckBox)c).Checked));

 var args = Buf($"{h},agree,uncheck");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());
 Assert.False(ReadOnGui(seq, "agree", c => ((CheckBox)c).Checked));
 }

 [Fact]
 public void Check_on_radio_sets_checked_true()
 {
 string h = OpenForm("radioform");
 AddControl(h, "opt1", "RADIO");
 int seq = SeqOf(h);

 var args = Buf($"{h},opt1,check");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());

 bool isChecked = ReadOnGui(seq, "opt1", c => ((RadioButton)c).Checked);
 Assert.True(isChecked);
 }

 [Fact]
 public void Click_on_checkbox_toggles_checked()
 {
 // CheckBox.PerformClick toggles Checked, just like a real
 // mouse click. This is the path the @FORMBIND
 // re-entrancy test will exercise via a real CheckedChanged
 // handler running on the callback worker.
 string h = OpenForm("toggleform");
 AddControl(h, "agree", "CHECKBOX");
 int seq = SeqOf(h);

 _plugin.f_FORMSIMULATE(Buf($"{h},agree,click"));
 Assert.True(ReadOnGui(seq, "agree", c => ((CheckBox)c).Checked));

 _plugin.f_FORMSIMULATE(Buf($"{h},agree,click"));
 Assert.False(ReadOnGui(seq, "agree", c => ((CheckBox)c).Checked));
 }

 [Fact]
 public void Settext_on_panel_does_not_throw()
 {
 // PANEL has no meaningful text, but settext writes
 // Control.Text on any control. Verifies the generic
 // settext path does not require a specific control type.
 string h = OpenForm("panelform");
 AddControl(h, "p1", "PANEL");

 var args = Buf($"{h},p1,settext,hello");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());
 }

 [Fact]
 public void Action_is_case_insensitive()
 {
 string h = OpenForm("caseform");
 AddControl(h, "btn", "BUTTON");

 var args = Buf($"{h},btn,CLICK");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());
 }

 // ---------- Lazy realization ----------

 [Fact]
 public void Simulate_lazily_realizes_when_not_yet_shown()
 {
 string h = OpenForm("lazyform");
 AddControl(h, "btn", "BUTTON");
 int seq = SeqOf(h);

 // Critical: no @FORMSHOW or @FORMSAVEIMAGE has been called.
 Assert.False(_plugin.IsRealized(seq));

 var args = Buf($"{h},btn,click");
 _plugin.f_FORMSIMULATE(args);
 Assert.Equal("0", args.ToString());
 Assert.True(_plugin.IsRealized(seq));
 }
 }
}
