// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;

using FormCast.Internal;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// Unit tests for the hand-rolled <see cref="JsonReader"/> and
 /// <see cref="JsonWriter"/> helpers used by the form template
 /// serializer. These also serve as the regression suite that the
 /// swap to <c>System.Text.Json</c> must keep green.
 /// </summary>
 public class JsonReaderWriterTests
 {
 [Fact]
 public void Reader_parses_empty_object()
 {
 var v = JsonReader.Parse("{}") as Dictionary<string, object?>;
 Assert.NotNull(v);
 Assert.Empty(v!);
 }

 [Fact]
 public void Reader_parses_simple_string_property()
 {
 var v = (Dictionary<string, object?>)JsonReader.Parse("{\"k\":\"v\"}")!;
 Assert.Equal("v", v["k"]);
 }

 [Fact]
 public void Reader_parses_integer_property()
 {
 var v = (Dictionary<string, object?>)JsonReader.Parse("{\"n\":42}")!;
 Assert.Equal(42, v["n"]);
 }

 [Fact]
 public void Reader_parses_negative_integer()
 {
 var v = (Dictionary<string, object?>)JsonReader.Parse("{\"n\":-7}")!;
 Assert.Equal(-7, v["n"]);
 }

 [Fact]
 public void Reader_parses_array_of_ints()
 {
 var arr = (List<object?>)JsonReader.Parse("[1, 2, 3]")!;
 Assert.Equal(3, arr.Count);
 Assert.Equal(1, arr[0]);
 Assert.Equal(2, arr[1]);
 Assert.Equal(3, arr[2]);
 }

 [Fact]
 public void Reader_parses_nested_object()
 {
 string src = "{\"outer\":{\"inner\":\"x\"}}";
 var v = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 var inner = (Dictionary<string, object?>)v["outer"]!;
 Assert.Equal("x", inner["inner"]);
 }

 [Fact]
 public void Reader_handles_string_escapes()
 {
 string src = "\"line\\nnext\\ttab\\\"quoted\\\\backslash\"";
 var s = (string)JsonReader.Parse(src)!;
 Assert.Equal("line\nnext\ttab\"quoted\\backslash", s);
 }

 [Fact]
 public void Reader_handles_unicode_escape()
 {
 var s = (string)JsonReader.Parse("\"\\u00e9\"")!;
 Assert.Equal("\u00e9", s);
 }

 [Fact]
 public void Reader_parses_booleans_and_null()
 {
 var arr = (List<object?>)JsonReader.Parse("[true, false, null]")!;
 Assert.Equal(true, arr[0]);
 Assert.Equal(false, arr[1]);
 Assert.Null(arr[2]);
 }

 [Fact]
 public void Reader_throws_on_trailing_garbage()
 {
 Assert.Throws<FormatException>(() => JsonReader.Parse("{} junk"));
 }

 [Fact]
 public void Reader_throws_on_unterminated_string()
 {
 Assert.Throws<FormatException>(() => JsonReader.Parse("\"abc"));
 }

 [Fact]
 public void Writer_emits_property_in_object()
 {
 var w = new JsonWriter();
 w.BeginObject();
 w.WriteProperty("name", "Tim");
 w.WriteProperty("age", 30);
 w.EndObject();
 string s = w.ToString();
 // Round-trip via reader to avoid being whitespace-sensitive.
 var d = (Dictionary<string, object?>)JsonReader.Parse(s)!;
 Assert.Equal("Tim", d["name"]);
 Assert.Equal(30, d["age"]);
 }

 [Fact]
 public void Writer_emits_array_with_object_elements()
 {
 var w = new JsonWriter();
 w.BeginObject();
 w.BeginArray("items");
 w.BeginArrayElementObject();
 w.WriteProperty("k", "a");
 w.EndObject();
 w.BeginArrayElementObject();
 w.WriteProperty("k", "b");
 w.EndObject();
 w.EndArray();
 w.EndObject();

 var d = (Dictionary<string, object?>)JsonReader.Parse(w.ToString())!;
 var arr = (List<object?>)d["items"]!;
 Assert.Equal(2, arr.Count);
 Assert.Equal("a", ((Dictionary<string, object?>)arr[0]!)["k"]);
 Assert.Equal("b", ((Dictionary<string, object?>)arr[1]!)["k"]);
 }

 [Fact]
 public void Writer_escapes_special_characters_in_strings()
 {
 var w = new JsonWriter();
 w.BeginObject();
 w.WriteProperty("v", "a\"b\\c\nd");
 w.EndObject();
 var d = (Dictionary<string, object?>)JsonReader.Parse(w.ToString())!;
 Assert.Equal("a\"b\\c\nd", d["v"]);
 }

 // -----------------------------------------------------------------
 // JSONC features: line and block comments, trailing commas
 // -----------------------------------------------------------------

 [Fact]
 public void Reader_skips_line_comment_before_property()
 {
 string src = "{\n // pick a name\n \"k\": \"v\"\n}";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal("v", d["k"]);
 }

 [Fact]
 public void Reader_skips_line_comment_at_end_of_line()
 {
 string src = "{\n \"k\": \"v\" // trailing remark\n}";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal("v", d["k"]);
 }

 [Fact]
 public void Reader_skips_block_comment()
 {
 string src = "{ /* comment block */ \"k\": \"v\" }";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal("v", d["k"]);
 }

 [Fact]
 public void Reader_skips_multi_line_block_comment()
 {
 string src = "{\n/* this comment\n spans multiple lines\n and contains \"quotes\" */\n \"k\": 1\n}";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal(1, d["k"]);
 }

 [Fact]
 public void Reader_skips_comment_inside_array()
 {
 string src = "[1, /* skip */ 2, // also skip\n 3]";
 var arr = (List<object?>)JsonReader.Parse(src)!;
 Assert.Equal(3, arr.Count);
 Assert.Equal(1, arr[0]);
 Assert.Equal(2, arr[1]);
 Assert.Equal(3, arr[2]);
 }

 [Fact]
 public void Reader_accepts_trailing_comma_in_object()
 {
 string src = "{\"a\": 1, \"b\": 2,}";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal(1, d["a"]);
 Assert.Equal(2, d["b"]);
 }

 [Fact]
 public void Reader_accepts_trailing_comma_in_array()
 {
 string src = "[1, 2, 3,]";
 var arr = (List<object?>)JsonReader.Parse(src)!;
 Assert.Equal(3, arr.Count);
 }

 [Fact]
 public void Reader_accepts_trailing_comma_with_comment()
 {
 string src = "{\n \"a\": 1, // last\n}";
 var d = (Dictionary<string, object?>)JsonReader.Parse(src)!;
 Assert.Equal(1, d["a"]);
 }

 [Fact]
 public void Reader_throws_on_unterminated_block_comment()
 {
 // Block comment never closed: System.Text.Json reports
 // this as a JsonException which we wrap as FormatException.
 Assert.Throws<FormatException>(() =>
 JsonReader.Parse("{ /* never closed \"k\": 1 }"));
 }
 }
}
