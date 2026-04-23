// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Globalization;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Round-trip fidelity tests for <see cref="FormSerializer"/>.
 /// </summary>
 public class FormSerializerTests
 {
 [Fact]
 public void Serialize_throws_on_null()
 {
 Assert.Throws<ArgumentNullException>(() => FormSerializer.Serialize(null!));
 }

 [Fact]
 public void Empty_form_round_trips()
 {
 var f = new FormDescriptor
 {
 Type = "form",
 Name = "settings",
 Title = "Settings",
 X = 10, Y = 20, Width = 400, Height = 300,
 LayoutMode = "absolute",
 };
 string json = FormSerializer.Serialize(f);
 FormDescriptor f2 = FormSerializer.Deserialize(json);

 Assert.Equal(f.Type, f2.Type);
 Assert.Equal(f.Name, f2.Name);
 Assert.Equal(f.Title, f2.Title);
 Assert.Equal(f.X, f2.X);
 Assert.Equal(f.Y, f2.Y);
 Assert.Equal(f.Width, f2.Width);
 Assert.Equal(f.Height, f2.Height);
 Assert.Equal(f.LayoutMode, f2.LayoutMode);
 Assert.Empty(f2.Controls);
 }

 [Fact]
 public void Form_with_controls_round_trips()
 {
 var f = new FormDescriptor
 {
 Type = "dialog",
 Name = "confirm",
 Title = "Confirm",
 X = 0, Y = 0, Width = 320, Height = 200,
 LayoutMode = "flow",
 };
 f.Controls.Add(new ControlDescriptor
 {
 Type = "LABEL", Id = "msg",
 X = 10, Y = 10, Width = 300, Height = 40,
 Text = "Are you sure?",
 });
 var btn = new ControlDescriptor
 {
 Type = "BUTTON", Id = "ok",
 X = 200, Y = 150, Width = 80, Height = 30,
 Text = "OK",
 };
 btn.Properties["default"] = "1";
 btn.Properties["dock"] = "right";
 f.Controls.Add(btn);

 string json = FormSerializer.Serialize(f);
 FormDescriptor f2 = FormSerializer.Deserialize(json);

 Assert.Equal(2, f2.Controls.Count);

 Assert.Equal("LABEL", f2.Controls[0].Type);
 Assert.Equal("msg", f2.Controls[0].Id);
 Assert.Equal("Are you sure?", f2.Controls[0].Text);
 Assert.Empty(f2.Controls[0].Properties);

 Assert.Equal("BUTTON", f2.Controls[1].Type);
 Assert.Equal("ok", f2.Controls[1].Id);
 Assert.Equal(2, f2.Controls[1].Properties.Count);
 Assert.Equal("1", f2.Controls[1].Properties["default"]);
 Assert.Equal("right", f2.Controls[1].Properties["dock"]);
 }

 [Fact]
 public void Round_trip_preserves_special_characters_in_text()
 {
 var f = new FormDescriptor
 {
 Type = "form", Name = "x", Title = "x",
 Width = 100, Height = 100,
 };
 f.Controls.Add(new ControlDescriptor
 {
 Type = "LABEL", Id = "lbl",
 Text = "line1\nline2\t\"quoted\"\\back",
 });
 string json = FormSerializer.Serialize(f);
 FormDescriptor f2 = FormSerializer.Deserialize(json);
 Assert.Equal("line1\nline2\t\"quoted\"\\back", f2.Controls[0].Text);
 }

 [Fact]
 public void Deserialize_uses_defaults_for_missing_fields()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\"}";
 FormDescriptor f = FormSerializer.Deserialize(json);
 Assert.Equal("form", f.Type);
 Assert.Equal("x", f.Name);
 Assert.Equal(string.Empty, f.Title);
 Assert.Equal(0, f.X);
 Assert.Equal(0, f.Y);
 Assert.Equal("absolute", f.LayoutMode);
 Assert.Empty(f.Controls);
 }

 [Fact]
 public void Deserialize_throws_when_root_is_not_object()
 {
 Assert.Throws<FormatException>(() => FormSerializer.Deserialize("[]"));
 }

 [Fact]
 public void Round_trip_preserves_form_level_property_bag()
 {
 // form.Properties round-trips through Serialize /
 // Deserialize. The property bag is the storage for layout
 // configuration knobs that @FORMRELAYOUT consumes.
 var f = new FormDescriptor
 {
 Type = "form", Name = "x", Title = "x",
 Width = 100, Height = 100,
 LayoutMode = "grid",
 };
 f.Properties["grid_rows"] = "3";
 f.Properties["grid_cols"] = "2";
 f.Properties["grid_padding"] = "8";

 string json = FormSerializer.Serialize(f);
 FormDescriptor f2 = FormSerializer.Deserialize(json);

 Assert.Equal("3", f2.Properties["grid_rows"]);
 Assert.Equal("2", f2.Properties["grid_cols"]);
 Assert.Equal("8", f2.Properties["grid_padding"]);
 }

 [Fact]
 public void Round_trip_omits_form_property_bag_when_empty()
 {
 // empty form.Properties must NOT emit a "props"
 // key on the form, so a template that does not need
 // layout knobs stays byte-identical to its form.
 var f = new FormDescriptor
 {
 Type = "form", Name = "x", Title = "x",
 Width = 100, Height = 100,
 };
 string json = FormSerializer.Serialize(f);
 Assert.DoesNotContain("\"props\"", json);
 }

 [Fact]
 public void Serialize_is_deterministic_across_repeated_calls()
 {
 // round-trip fidelity: serializing the same descriptor
 // twice must produce byte-identical output. The writer
 // backed by Utf8JsonWriter is order-preserving and the
 // descriptor exposes Controls in declaration order, so
 // determinism follows from the source iteration order.
 var f = new FormDescriptor
 {
 Type = "form", Name = "complex", Title = "Complex Form",
 X = 50, Y = 60, Width = 800, Height = 600,
 LayoutMode = "grid",
 };
 f.Properties["grid_rows"] = "4";
 f.Properties["grid_cols"] = "3";
 for (int i = 0; i < 12; i++)
 {
 var c = new ControlDescriptor
 {
 Type = (i % 2 == 0) ? "LABEL" : "EDIT",
 Id = "ctl" + i.ToString(CultureInfo.InvariantCulture),
 X = i * 10, Y = i * 5, Width = 100, Height = 20,
 Text = "item " + i.ToString(CultureInfo.InvariantCulture),
 };
 c.Properties["row"] = (i / 3).ToString(CultureInfo.InvariantCulture);
 c.Properties["col"] = (i % 3).ToString(CultureInfo.InvariantCulture);
 f.Controls.Add(c);
 }

 string a = FormSerializer.Serialize(f);
 string b = FormSerializer.Serialize(f);
 Assert.Equal(a, b);
 }

 [Fact]
 public void Save_load_save_cycle_is_byte_stable()
 {
 // round-trip fidelity: serializing -> deserializing
 // -> reserializing must produce the same JSON as the
 // first serialization. This is the "save, edit, save"
 // contract template authors rely on.
 var f = new FormDescriptor
 {
 Type = "form", Name = "rt", Title = "Round Trip",
 X = 10, Y = 20, Width = 400, Height = 300,
 LayoutMode = "grid",
 };
 f.Properties["grid_rows"] = "2";
 f.Properties["grid_cols"] = "2";
 f.Controls.Add(new ControlDescriptor
 {
 Type = "BUTTON", Id = "ok", Text = "OK",
 X = 10, Y = 10, Width = 80, Height = 24,
 });
 var checkbox = new ControlDescriptor
 {
 Type = "CHECKBOX", Id = "agree", Text = "Agree",
 X = 100, Y = 10, Width = 120, Height = 20,
 };
 checkbox.Properties["checked"] = "1";
 f.Controls.Add(checkbox);

 string first = FormSerializer.Serialize(f);
 FormDescriptor reloaded = FormSerializer.Deserialize(first);
 string second = FormSerializer.Serialize(reloaded);
 Assert.Equal(first, second);
 }

 [Fact]
 public void Deserialize_accepts_jsonc_template_with_comments_and_trailing_commas()
 {
 // a real-world template authored by a human will
 // have comments and may have trailing commas. The
 // System.Text.Json reader (with CommentHandling.Skip and
 // AllowTrailingCommas) handles both. This test pins the
 // FormSerializer-level contract that JSONC templates
 // round-trip through the public API.
 string jsonc = @"{
 // FormCast settings template
 ""version"": 1,
 ""type"": ""form"",
 ""name"": ""settings"",
 ""title"": ""Settings"",
 /* default position is centered;
 callers may override at @FORMOPEN time */
 ""x"": 100,
 ""y"": 100,
 ""width"": 400,
 ""height"": 300,
 ""layout"": ""absolute"",
 ""controls"": [
 {
 ""type"": ""LABEL"",
 ""id"": ""lbl"",
 ""x"": 10, ""y"": 10, ""width"": 200, ""height"": 20,
 ""text"": ""Username:"", // tooltip would be a future addition
 },
 {
 ""type"": ""EDIT"",
 ""id"": ""user"",
 ""x"": 10, ""y"": 35, ""width"": 200, ""height"": 24,
 ""text"": """",
 },
 ],
}";

 FormDescriptor f = FormSerializer.Deserialize(jsonc);
 Assert.Equal("form", f.Type);
 Assert.Equal("settings", f.Name);
 Assert.Equal("Settings", f.Title);
 Assert.Equal(100, f.X);
 Assert.Equal(100, f.Y);
 Assert.Equal(400, f.Width);
 Assert.Equal(300, f.Height);
 Assert.Equal(2, f.Controls.Count);
 Assert.Equal("LABEL", f.Controls[0].Type);
 Assert.Equal("lbl", f.Controls[0].Id);
 Assert.Equal("Username:", f.Controls[0].Text);
 Assert.Equal("EDIT", f.Controls[1].Type);
 Assert.Equal("user", f.Controls[1].Id);
 }
 }
}
