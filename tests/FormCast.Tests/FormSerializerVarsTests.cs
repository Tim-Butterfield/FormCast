// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Tests for <c>${var}</c> substitution in
 /// <see cref="FormSerializer.Deserialize(string, IDictionary{string, string}?)"/>
 /// plus the BTM-side <see cref="FormSerializer.ParseVars"/> helper.
 /// </summary>
 public sealed class FormSerializerVarsTests
 {
 // -----------------------------------------------------------------
 // ParseVars helper
 // -----------------------------------------------------------------

 [Fact]
 public void ParseVars_null_returns_empty_dictionary()
 {
 var d = FormSerializer.ParseVars(null);
 Assert.Empty(d);
 }

 [Fact]
 public void ParseVars_empty_returns_empty_dictionary()
 {
 var d = FormSerializer.ParseVars(string.Empty);
 Assert.Empty(d);
 }

 [Fact]
 public void ParseVars_single_key_value()
 {
 var d = FormSerializer.ParseVars("name=Tim");
 Assert.Equal("Tim", d["name"]);
 }

 [Fact]
 public void ParseVars_multiple_segments_split_on_pipe()
 {
 var d = FormSerializer.ParseVars("name=Tim|age=42|city=Bend");
 Assert.Equal("Tim", d["name"]);
 Assert.Equal("42", d["age"]);
 Assert.Equal("Bend", d["city"]);
 }

 [Fact]
 public void ParseVars_value_can_contain_equals()
 {
 // Only the first '=' splits; everything after is value.
 var d = FormSerializer.ParseVars("expr=a=b+c");
 Assert.Equal("a=b+c", d["expr"]);
 }

 [Fact]
 public void ParseVars_segment_without_equals_becomes_empty_value()
 {
 var d = FormSerializer.ParseVars("flag");
 Assert.Equal(string.Empty, d["flag"]);
 }

 [Fact]
 public void ParseVars_trims_key_whitespace_preserves_value_whitespace()
 {
 var d = FormSerializer.ParseVars(" name = Tim Butterfield ");
 Assert.True(d.ContainsKey("name"));
 Assert.Equal(" Tim Butterfield ", d["name"]);
 }

 // -----------------------------------------------------------------
 // Substitution: form-level fields
 // -----------------------------------------------------------------

 [Fact]
 public void Deserialize_substitutes_form_level_string_field()
 {
 string json = "{\"type\":\"form\",\"name\":\"${formname}\",\"title\":\"${title}\",\"width\":100,\"height\":100}";
 var vars = new Dictionary<string, string>
 {
 ["formname"] = "settings",
 ["title"] = "Settings",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("settings", f.Name);
 Assert.Equal("Settings", f.Title);
 }

 [Fact]
 public void Deserialize_substitutes_inside_string_with_surrounding_text()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"Hello, ${name}!\"}";
 var vars = new Dictionary<string, string> { ["name"] = "Tim" };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("Hello, Tim!", f.Title);
 }

 [Fact]
 public void Deserialize_substitutes_same_placeholder_multiple_times()
 {
 string json = "{\"type\":\"form\",\"name\":\"${who}\",\"title\":\"Welcome, ${who}!\"}";
 var vars = new Dictionary<string, string> { ["who"] = "Tim" };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("Tim", f.Name);
 Assert.Equal("Welcome, Tim!", f.Title);
 }

 // -----------------------------------------------------------------
 // Substitution: numeric fields via string fall-through
 // -----------------------------------------------------------------

 [Fact]
 public void Deserialize_substitutes_into_numeric_field_via_string_form()
 {
 // a numeric field expressed as "width": "${w}" works
 // because ReadInt falls through to int.TryParse on string
 // values.
 string json = "{\"type\":\"form\",\"name\":\"x\",\"width\":\"${w}\",\"height\":\"${h}\"}";
 var vars = new Dictionary<string, string>
 {
 ["w"] = "640",
 ["h"] = "480",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal(640, f.Width);
 Assert.Equal(480, f.Height);
 }

 // -----------------------------------------------------------------
 // Substitution: control fields
 // -----------------------------------------------------------------

 [Fact]
 public void Deserialize_substitutes_control_text_field()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"controls\":[" +
 "{\"type\":\"LABEL\",\"id\":\"lbl\",\"text\":\"${prompt}\"}]}";
 var vars = new Dictionary<string, string> { ["prompt"] = "Enter name:" };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Single(f.Controls);
 Assert.Equal("Enter name:", f.Controls[0].Text);
 }

 [Fact]
 public void Deserialize_substitutes_inside_control_property_bag()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"controls\":[" +
 "{\"type\":\"BUTTON\",\"id\":\"go\",\"text\":\"OK\"," +
 "\"props\":{\"tooltip\":\"Click to ${verb}\"}}]}";
 var vars = new Dictionary<string, string> { ["verb"] = "submit" };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("Click to submit", f.Controls[0].Properties["tooltip"]);
 }

 // -----------------------------------------------------------------
 // Strict-by-default error handling
 // -----------------------------------------------------------------

 [Fact]
 public void Deserialize_with_vars_throws_on_unresolved_placeholder()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"${missing}\"}";
 var vars = new Dictionary<string, string> { ["other"] = "value" };
 FormatException ex = Assert.Throws<FormatException>(() =>
 FormSerializer.Deserialize(json, vars));
 Assert.Contains("missing", ex.Message);
 }

 [Fact]
 public void Deserialize_with_null_vars_leaves_placeholders_literal()
 {
 // passing vars=null disables substitution entirely.
 // A template with a placeholder is preserved verbatim, so
 // a name like "${not-substituted}" survives the round trip.
 string json = "{\"type\":\"form\",\"name\":\"${literal}\",\"title\":\"plain\"}";
 FormDescriptor f = FormSerializer.Deserialize(json, vars: null);
 Assert.Equal("${literal}", f.Name);
 Assert.Equal("plain", f.Title);
 }

 [Fact]
 public void Deserialize_with_empty_vars_throws_on_any_placeholder()
 {
 // An empty (but non-null) vars dictionary still activates
 // strict mode: a template with any placeholder errors
 // because no key resolves.
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"${anything}\"}";
 var vars = new Dictionary<string, string>();
 Assert.Throws<FormatException>(() =>
 FormSerializer.Deserialize(json, vars));
 }

 // -----------------------------------------------------------------
 // Edge cases
 // -----------------------------------------------------------------

 [Fact]
 public void Deserialize_does_not_recursively_expand_substituted_values()
 {
 // Single-pass substitution: if vars[a] = "${b}", the
 // resulting field value is the literal "${b}", NOT the
 // value of b. Prevents accidental loops and is much
 // simpler to reason about.
 string json = "{\"type\":\"form\",\"name\":\"${a}\",\"title\":\"plain\"}";
 var vars = new Dictionary<string, string>
 {
 ["a"] = "${b}",
 ["b"] = "should-not-appear",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("${b}", f.Name);
 }

 [Fact]
 public void Deserialize_leaves_empty_placeholder_literal()
 {
 // ${} does not match the [A-Za-z_][A-Za-z0-9_]* grammar,
 // so it is preserved verbatim rather than treated as a
 // missing variable.
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"a${}b\"}";
 var vars = new Dictionary<string, string>();
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("a${}b", f.Title);
 }

 [Fact]
 public void Deserialize_leaves_dollar_followed_by_non_brace_literal()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"price: $5.00\"}";
 var vars = new Dictionary<string, string>();
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("price: $5.00", f.Title);
 }

 [Fact]
 public void Deserialize_substitution_is_case_sensitive()
 {
 // ${Name} and ${name} are distinct keys; the substitution
 // dictionary uses Ordinal comparison.
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"${Name}\"}";
 var vars = new Dictionary<string, string> { ["name"] = "lowercase" };
 Assert.Throws<FormatException>(() =>
 FormSerializer.Deserialize(json, vars));
 }

 [Fact]
 public void Deserialize_substitutes_back_to_back_placeholders_without_separator()
 {
 string json = "{\"type\":\"form\",\"name\":\"x\",\"title\":\"${a}${b}\"}";
 var vars = new Dictionary<string, string>
 {
 ["a"] = "Hello",
 ["b"] = "World",
 };
 FormDescriptor f = FormSerializer.Deserialize(json, vars);
 Assert.Equal("HelloWorld", f.Title);
 }
 }
}
