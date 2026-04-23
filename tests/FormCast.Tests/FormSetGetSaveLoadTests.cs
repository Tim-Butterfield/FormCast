// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System.IO;
using System.Text;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Dispatch-level tests for <c>@FORMSET</c>, <c>@FORMGET</c>,
 /// <c>@FORMSAVE</c>, and <c>@FORMLOAD</c> from <see cref="FormCast.Plugin"/>.
 /// </summary>
 public class FormSetGetSaveLoadTests
 {
 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private static string OpenForm(global::FormCast.Plugin p)
 {
 var args = Buf("form,test,10,20,400,300");
 p.f_FORMOPEN(args);
 return args.ToString();
 }

 private static void AddButton(global::FormCast.Plugin p, string h, string id)
 {
 var args = Buf($"{h},{id},BUTTON,100,200,80,30,OK");
 p.f_FORMADD(args);
 }

 // ---- FORMSET ----

 [Fact]
 public void FORMSET_form_title_succeeds()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 var args = Buf($"{h},.,title,New Title");
 p.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var get = Buf($"{h},.,title");
 p.f_FORMGET(get);
 Assert.Equal("New Title", get.ToString());
 }

 [Fact]
 public void FORMSET_form_int_property_parses_value()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 var args = Buf($"{h},,width,500");
 p.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var get = Buf($"{h},,width");
 p.f_FORMGET(get);
 Assert.Equal("500", get.ToString());
 }

 [Fact]
 public void FORMSET_unknown_form_property_lands_in_form_property_bag()
 {
 // form-level FORMSET now mirrors the control-level
 // behavior -- unknown well-known field falls through to
 // form.Properties (the layout-config knob extension point).
 // The previous behavior of returning 20104
 // ErrUnknownProperty was changed when @FORMRELAYOUT
 // gained a need to read user-supplied layout knobs like
 // grid_rows / flow_hgap from the form descriptor.
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 var setArgs = Buf($"{h},.,grid_rows,3");
 p.f_FORMSET(setArgs);
 Assert.Equal("0", setArgs.ToString());

 var getArgs = Buf($"{h},.,grid_rows");
 p.f_FORMGET(getArgs);
 Assert.Equal("3", getArgs.ToString());
 }

 [Fact]
 public void FORMSET_control_text_succeeds()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 AddButton(p, h, "ok");

 var args = Buf($"{h},ok,text,Click Me");
 p.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var get = Buf($"{h},ok,text");
 p.f_FORMGET(get);
 Assert.Equal("Click Me", get.ToString());
 }

 [Fact]
 public void FORMSET_control_unknown_property_lands_in_property_bag()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 AddButton(p, h, "ok");

 var args = Buf($"{h},ok,dock,right");
 p.f_FORMSET(args);
 Assert.Equal("0", args.ToString());

 var get = Buf($"{h},ok,dock");
 p.f_FORMGET(get);
 Assert.Equal("right", get.ToString());
 }

 [Fact]
 public void FORMSET_unknown_control_id_returns_error()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);

 var args = Buf($"{h},ghost,text,x");
 p.f_FORMSET(args);
 Assert.Equal("20103", args.ToString());
 }

 [Fact]
 public void FORMSET_invalid_handle_returns_error()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("not-a-handle,.,title,x");
 p.f_FORMSET(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMSET_wrong_arg_count_returns_error()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("a,b,c");
 p.f_FORMSET(args);
 Assert.Equal("20101", args.ToString());
 }

 // ---- FORMGET ----

 [Fact]
 public void FORMGET_invalid_handle_returns_empty_buffer()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("garbage,.,title");
 p.f_FORMGET(args);
 Assert.Equal(string.Empty, args.ToString());
 }

 [Fact]
 public void FORMGET_unknown_property_returns_empty_buffer()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 var args = Buf($"{h},.,nope");
 p.f_FORMGET(args);
 Assert.Equal(string.Empty, args.ToString());
 }

 [Fact]
 public void FORMGET_form_layout_returns_default_absolute()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 var args = Buf($"{h},.,layout");
 p.f_FORMGET(args);
 Assert.Equal("absolute", args.ToString());
 }

 [Fact]
 public void FORMGET_controls_returns_count()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 AddButton(p, h, "a");
 AddButton(p, h, "b");
 var args = Buf($"{h},.,controls");
 p.f_FORMGET(args);
 Assert.Equal("2", args.ToString());
 }

 // ---- FORMSAVE / FORMLOAD ----

 [Fact]
 public void FORMSAVE_then_FORMLOAD_round_trips()
 {
 var p = new global::FormCast.Plugin();
 string h = OpenForm(p);
 AddButton(p, h, "ok");

 // Mutate something to make the save meaningful.
 var setTitle = Buf($"{h},.,title,Round Trip");
 p.f_FORMSET(setTitle);
 var setText = Buf($"{h},ok,text,Apply");
 p.f_FORMSET(setText);
 var setDock = Buf($"{h},ok,dock,bottom");
 p.f_FORMSET(setDock);

 string path = Path.Combine(Path.GetTempPath(),
 $"formcast_test_{System.Guid.NewGuid():N}.json");
 try
 {
 var save = Buf($"{h},{path}");
 p.f_FORMSAVE(save);
 Assert.Equal("0", save.ToString());
 Assert.True(File.Exists(path));

 var load = Buf(path);
 p.f_FORMLOAD(load);
 string newHandle = load.ToString();
 Assert.NotEqual(string.Empty, newHandle);
 Assert.NotEqual(h, newHandle); // fresh handle on load

 var getTitle = Buf($"{newHandle},.,title");
 p.f_FORMGET(getTitle);
 Assert.Equal("Round Trip", getTitle.ToString());

 var getText = Buf($"{newHandle},ok,text");
 p.f_FORMGET(getText);
 Assert.Equal("Apply", getText.ToString());

 var getDock = Buf($"{newHandle},ok,dock");
 p.f_FORMGET(getDock);
 Assert.Equal("bottom", getDock.ToString());

 var getCount = Buf($"{newHandle},.,controls");
 p.f_FORMGET(getCount);
 Assert.Equal("1", getCount.ToString());
 }
 finally
 {
 if (File.Exists(path)) { File.Delete(path); }
 }
 }

 [Fact]
 public void FORMSAVE_invalid_handle_returns_error()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("garbage,/tmp/x.json");
 p.f_FORMSAVE(args);
 Assert.Equal("20100", args.ToString());
 }

 [Fact]
 public void FORMSAVE_wrong_arg_count_returns_error()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf("only-one");
 p.f_FORMSAVE(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMLOAD_missing_file_returns_empty_buffer()
 {
 var p = new global::FormCast.Plugin();
 var args = Buf(Path.Combine(Path.GetTempPath(),
 $"formcast_missing_{System.Guid.NewGuid():N}.json"));
 p.f_FORMLOAD(args);
 Assert.Equal(string.Empty, args.ToString());
 }

 [Fact]
 public void FORMLOAD_invalid_json_returns_empty_buffer()
 {
 var p = new global::FormCast.Plugin();
 string path = Path.Combine(Path.GetTempPath(),
 $"formcast_bad_{System.Guid.NewGuid():N}.json");
 File.WriteAllText(path, "{ not valid");
 try
 {
 var args = Buf(path);
 p.f_FORMLOAD(args);
 Assert.Equal(string.Empty, args.ToString());
 }
 finally
 {
 if (File.Exists(path)) { File.Delete(path); }
 }
 }
 }
}
