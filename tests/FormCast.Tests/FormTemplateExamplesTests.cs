// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// round-trip every JSONC example template under
 /// <c>examples/templates/</c> through
 /// <see cref="FormSerializer"/>. Each example must (a) parse
 /// without error (with vars supplied where the template uses
 /// placeholders), (b) materialize a sensible
 /// <see cref="FormDescriptor"/>, and (c) survive a
 /// serialize-deserialize-serialize round trip with byte-stable
 /// output. The test acts as the runtime contract enforcement
 /// that the published schema and the C# parser stay in sync
 /// with the example templates that ship in the repo.
 /// </summary>
 public sealed class FormTemplateExamplesTests
 {
 /// <summary>
 /// Locate <c>examples/templates/</c> by walking up from the
 /// test runner's <see cref="AppDomain.BaseDirectory"/> until
 /// a sibling directory called <c>examples</c> appears. We
 /// cannot use <c>Assembly.Location</c> because the test
 /// runner shadow-copies the test DLL into a temp directory
 /// far from the repo root. <c>AppDomain.BaseDirectory</c>
 /// stays anchored to the original
 /// <c>tests/FormCast.Tests/bin/Release/net48/</c>, which is
 /// four levels below the repo root, so the walk reaches
 /// <c>examples/templates/</c> in a small bounded number of
 /// steps.
 /// </summary>
 private static string ExamplesDir
 {
 get
 {
 string? dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(
 Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
 while (!string.IsNullOrEmpty(dir))
 {
 string candidate = Path.Combine(dir, "examples", "templates");
 if (Directory.Exists(candidate)) { return candidate; }
 DirectoryInfo? parent = Directory.GetParent(dir);
 if (parent is null) { break; }
 dir = parent.FullName;
 }
 throw new DirectoryNotFoundException(
 "Could not locate examples/templates/ by walking up from " +
 AppDomain.CurrentDomain.BaseDirectory);
 }
 }

 // -----------------------------------------------------------------
 // simple.jsonc
 // -----------------------------------------------------------------

 [Fact]
 public void Simple_template_loads_and_round_trips()
 {
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "simple.jsonc"));
 FormDescriptor f = FormSerializer.Deserialize(json);

 Assert.Equal("form", f.Type);
 Assert.Equal("simple", f.Name);
 Assert.Equal("Simple Form", f.Title);
 Assert.Equal(320, f.Width);
 Assert.Equal(120, f.Height);
 Assert.Equal("absolute", f.LayoutMode);
 Assert.Equal(2, f.Controls.Count);
 Assert.Equal("LABEL", f.Controls[0].Type);
 Assert.Equal("lbl", f.Controls[0].Id);
 Assert.Equal("Hello, FormCast.", f.Controls[0].Text);
 Assert.Equal("BUTTON", f.Controls[1].Type);
 Assert.Equal("ok", f.Controls[1].Id);

 AssertRoundTripStable(f);
 }

 // -----------------------------------------------------------------
 // settings.jsonc
 // -----------------------------------------------------------------

 [Fact]
 public void Settings_template_loads_form_level_prop_bag_and_round_trips()
 {
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "settings.jsonc"));
 FormDescriptor f = FormSerializer.Deserialize(json);

 Assert.Equal("settings", f.Name);
 Assert.Equal("grid", f.LayoutMode);
 // Form-level prop bag round-trips: FormLayoutFactory will
 // pick these up at @FORMRELAYOUT time.
 Assert.Equal("4", f.Properties["grid_rows"]);
 Assert.Equal("2", f.Properties["grid_cols"]);
 Assert.Equal("8", f.Properties["grid_hgap"]);
 Assert.Equal(8, f.Controls.Count);

 // Per-control prop bag (row/col) round-trips too.
 ControlDescriptor lblName = f.Controls.First(c => c.Id == "lblName");
 Assert.Equal("0", lblName.Properties["row"]);
 Assert.Equal("0", lblName.Properties["col"]);

 ControlDescriptor btnOK = f.Controls.First(c => c.Id == "btnOK");
 Assert.Equal("3", btnOK.Properties["row"]);
 Assert.Equal("1", btnOK.Properties["col"]);

 AssertRoundTripStable(f);
 }

 // -----------------------------------------------------------------
 // vars.jsonc
 // -----------------------------------------------------------------

 [Fact]
 public void Vars_template_substitutes_placeholders_and_round_trips()
 {
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "vars.jsonc"));
 var vars = new Dictionary<string, string>
 {
 ["formname"] = "rename",
 ["title"] = "Rename file",
 ["w"] = "420",
 ["h"] = "140",
 ["prompt"] = "New name:",
 ["default"] = "untitled.txt",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);

 Assert.Equal("rename", f.Name);
 Assert.Equal("Rename file", f.Title);
 Assert.Equal(420, f.Width);
 Assert.Equal(140, f.Height);
 Assert.Equal("New name:", f.Controls.First(c => c.Id == "lbl").Text);
 Assert.Equal("untitled.txt", f.Controls.First(c => c.Id == "txt").Text);

 // Round-trip the SUBSTITUTED descriptor (not the raw
 // template). The output is a concrete form with literal
 // values; reloading should reproduce it.
 AssertRoundTripStable(f);
 }

 [Fact]
 public void Vars_template_loads_with_different_vars_to_a_different_form()
 {
 // Same template, different vars -> different form. This is
 // the design's "one template powers many BTM dialogs" use
 // case from PLUGIN_DESIGN.md section 6.17.
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "vars.jsonc"));
 var vars = new Dictionary<string, string>
 {
 ["formname"] = "search",
 ["title"] = "Find",
 ["w"] = "500",
 ["h"] = "120",
 ["prompt"] = "Search for:",
 ["default"] = "",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);

 Assert.Equal("search", f.Name);
 Assert.Equal("Find", f.Title);
 Assert.Equal(500, f.Width);
 Assert.Equal(120, f.Height);
 Assert.Equal("Search for:", f.Controls.First(c => c.Id == "lbl").Text);
 Assert.Equal(string.Empty, f.Controls.First(c => c.Id == "txt").Text);
 }

 [Fact]
 public void Vars_template_without_vars_preserves_placeholders_literally()
 {
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "vars.jsonc"));
 FormDescriptor f = FormSerializer.Deserialize(json, vars: null);

 // Placeholders survive verbatim when vars=null.
 Assert.Equal("${formname}", f.Name);
 Assert.Equal("${title}", f.Title);
 // Numeric fields with a string-typed placeholder fall back
 // to ReadInt's default (0) because TryParse rejects "${w}".
 Assert.Equal(0, f.Width);
 Assert.Equal(0, f.Height);
 }

 // -----------------------------------------------------------------
 // flow.jsonc
 // -----------------------------------------------------------------

 [Fact]
 public void Flow_template_loads_and_round_trips()
 {
 string json = File.ReadAllText(Path.Combine(ExamplesDir, "flow.jsonc"));
 FormDescriptor f = FormSerializer.Deserialize(json);

 Assert.Equal("toolbar", f.Name);
 Assert.Equal("flow", f.LayoutMode);
 Assert.Equal("6", f.Properties["flow_hgap"]);
 Assert.Equal("horizontal", f.Properties["flow_direction"]);
 Assert.Equal("true", f.Properties["flow_wrap"]);
 Assert.Equal(4, f.Controls.Count);
 Assert.All(f.Controls, c => Assert.Equal("BUTTON", c.Type));

 AssertRoundTripStable(f);
 }

 // -----------------------------------------------------------------
 // Discovery: every .jsonc file in examples/templates/ must
 // load via FormSerializer with no exception (no vars supplied
 // for files that have placeholders -- those should still parse
 // because vars=null disables substitution).
 // -----------------------------------------------------------------

 [Fact]
 public void Every_example_template_loads_with_null_vars()
 {
 string[] files = Directory.GetFiles(ExamplesDir, "*.jsonc");
 Assert.NotEmpty(files);
 foreach (string file in files)
 {
 string json = File.ReadAllText(file);
 // No vars: placeholders preserved literally; this is
 // the "smoke test that parsing succeeds" path.
 FormDescriptor f = FormSerializer.Deserialize(json, vars: null);
 Assert.NotNull(f);
 Assert.NotEmpty(f.Type);
 }
 }

 // -----------------------------------------------------------------
 // Helper: serialize -> deserialize -> serialize byte equality
 // -----------------------------------------------------------------

 private static void AssertRoundTripStable(FormDescriptor f)
 {
 string a = FormSerializer.Serialize(f);
 FormDescriptor reloaded = FormSerializer.Deserialize(a);
 string b = FormSerializer.Serialize(reloaded);
 Assert.Equal(a, b);
 }
 }
}
