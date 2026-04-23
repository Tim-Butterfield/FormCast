// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.IO;
using System.Text;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>@FORMIMPORT</c> in load a template from disk
 /// and append its controls to an existing form descriptor. Form-
 /// level fields of the imported template are ignored; only the
 /// control list is consumed. Collisions on control id are
 /// rejected fail-atomically with 20103.
 /// </summary>
 public sealed class FormImportTests : IDisposable
 {
 private readonly global::FormCast.Plugin _plugin;
 private readonly string _tempPath;

 public FormImportTests()
 {
 _plugin = new global::FormCast.Plugin();
 _tempPath = Path.Combine(
 Path.GetTempPath(),
 "fc_import_" + Guid.NewGuid().ToString("N") + ".jsonc");
 }

 public void Dispose()
 {
 if (File.Exists(_tempPath)) { File.Delete(_tempPath); }
 }

 private static StringBuilder Buf(string s = "") => new StringBuilder(s);

 private string OpenForm(string name = "host", int w = 400, int h = 300)
 {
 var args = Buf($"form,{name},10,20,{w},{h}");
 _plugin.f_FORMOPEN(args);
 return args.ToString();
 }

 private void AddControl(string handle, string id, string type = "BUTTON",
 int x = 5, int y = 5, int w = 80, int h = 24, string text = "")
 {
 var args = Buf($"{handle},{id},{type},{x},{y},{w},{h},{text}");
 _plugin.f_FORMADD(args);
 }

 private string Import(string handle, string path)
 {
 var args = Buf($"{handle},{path}");
 _plugin.f_FORMIMPORT(args);
 return args.ToString();
 }

 private void WriteTemplate(FormDescriptor template)
 {
 File.WriteAllText(_tempPath, FormSerializer.Serialize(template), Encoding.UTF8);
 }

 // -----------------------------------------------------------------
 // Validation paths
 // -----------------------------------------------------------------

 [Fact]
 public void FORMIMPORT_wrong_arg_count_returns_bad_args()
 {
 string h = OpenForm();
 var args = Buf(h);
 _plugin.f_FORMIMPORT(args);
 Assert.Equal("20101", args.ToString());
 }

 [Fact]
 public void FORMIMPORT_unparseable_handle_returns_invalid_handle()
 {
 string rc = Import("not-a-handle", _tempPath);
 Assert.Equal("20100", rc);
 }

 [Fact]
 public void FORMIMPORT_unknown_handle_returns_invalid_handle()
 {
 string rc = Import("L:99999:99", _tempPath);
 Assert.Equal("20100", rc);
 }

 [Fact]
 public void FORMIMPORT_missing_file_returns_io_failure()
 {
 string h = OpenForm();
 string rc = Import(h, _tempPath); // file does not exist yet
 Assert.Equal("20105", rc);
 }

 [Fact]
 public void FORMIMPORT_invalid_json_returns_parse_failure()
 {
 string h = OpenForm();
 File.WriteAllText(_tempPath, "{ this is not json }", Encoding.UTF8);
 string rc = Import(h, _tempPath);
 Assert.Equal("20106", rc);
 }

 // -----------------------------------------------------------------
 // Happy path
 // -----------------------------------------------------------------

 [Fact]
 public void FORMIMPORT_appends_template_controls_to_existing_form()
 {
 string h = OpenForm();
 AddControl(h, "existing");

 var template = new FormDescriptor
 {
 Type = "form", Name = "partial", Title = "ignored",
 X = 999, Y = 999, Width = 999, Height = 999,
 };
 template.Controls.Add(new ControlDescriptor
 {
 Type = "LABEL", Id = "imported_lbl", Text = "Hi",
 X = 10, Y = 10, Width = 100, Height = 20,
 });
 template.Controls.Add(new ControlDescriptor
 {
 Type = "EDIT", Id = "imported_edit",
 X = 10, Y = 35, Width = 200, Height = 24,
 });
 WriteTemplate(template);

 string rc = Import(h, _tempPath);
 Assert.Equal("0", rc);

 // 1 existing + 2 imported = 3
 var countArgs = Buf($"{h},.,controls");
 _plugin.f_FORMGET(countArgs);
 Assert.Equal("3", countArgs.ToString());
 }

 [Fact]
 public void FORMIMPORT_ignores_template_form_level_fields()
 {
 string h = OpenForm("host", 400, 300);
 var template = new FormDescriptor
 {
 Type = "dialog", Name = "ignored", Title = "Ignored",
 X = 999, Y = 999, Width = 999, Height = 999,
 LayoutMode = "grid",
 };
 template.Controls.Add(new ControlDescriptor
 {
 Type = "BUTTON", Id = "btn",
 X = 5, Y = 5, Width = 80, Height = 24,
 });
 WriteTemplate(template);

 Assert.Equal("0", Import(h, _tempPath));

 // Host form's title/width/etc unchanged.
 var get = Buf($"{h},.,name");
 _plugin.f_FORMGET(get);
 Assert.Equal("host", get.ToString());

 get = Buf($"{h},.,width");
 _plugin.f_FORMGET(get);
 Assert.Equal("400", get.ToString());

 get = Buf($"{h},.,layout");
 _plugin.f_FORMGET(get);
 Assert.Equal("absolute", get.ToString());
 }

 [Fact]
 public void FORMIMPORT_collision_returns_unknown_control_id_and_appends_nothing()
 {
 // fail-atomic policy: if any imported control's id
 // collides with an existing control's id, the entire
 // import is rejected and ZERO controls are appended.
 string h = OpenForm();
 AddControl(h, "ok");
 AddControl(h, "cancel");

 var template = new FormDescriptor { Type = "form", Name = "p" };
 template.Controls.Add(new ControlDescriptor
 {
 Type = "BUTTON", Id = "fresh1",
 Width = 80, Height = 24,
 });
 template.Controls.Add(new ControlDescriptor
 {
 Type = "BUTTON", Id = "OK", // collides (case-insensitive)
 Width = 80, Height = 24,
 });
 template.Controls.Add(new ControlDescriptor
 {
 Type = "BUTTON", Id = "fresh2",
 Width = 80, Height = 24,
 });
 WriteTemplate(template);

 string rc = Import(h, _tempPath);
 Assert.Equal("20103", rc);

 // Still 2 controls; fresh1 / fresh2 must NOT have been
 // appended even though they would have been valid.
 var get = Buf($"{h},.,controls");
 _plugin.f_FORMGET(get);
 Assert.Equal("2", get.ToString());
 }
 }
}
