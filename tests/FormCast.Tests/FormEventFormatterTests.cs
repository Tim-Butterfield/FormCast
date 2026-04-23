// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using FormCast.Forms;

using Xunit;

namespace FormCast.Tests
{
 /// <summary>
 /// unit tests for <see cref="FormEventFormatter"/>. Pin the
 /// line shape that <c>FORMEVENTS</c> writes via <c>wwriteXP</c>
 /// and that BTM scripts parse via <c>%@word[N, ,%ev]</c>.
 /// </summary>
 public class FormEventFormatterTests
 {
 [Fact]
 public void Click_event_without_payload_renders_three_tokens()
 {
 var ev = new FormEvent(5, "go", "click", string.Empty);
 Assert.Equal("5 click go", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Change_event_with_text_payload_renders_four_tokens()
 {
 var ev = new FormEvent(7, "name", "change", "hello");
 Assert.Equal("7 change name hello", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Change_event_with_multi_word_payload_keeps_data_inline()
 {
 // Data is the trailing field, so embedded spaces are
 // recovered by everything-after-word-3 in BTM.
 var ev = new FormEvent(7, "name", "change", "hello world");
 Assert.Equal("7 change name hello world", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Form_level_event_uses_dot_for_control_id()
 {
 var ev = new FormEvent(3, string.Empty, "close", string.Empty);
 Assert.Equal("3 close .", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Bool_payload_for_check_event_renders_literal()
 {
 var ev = new FormEvent(9, "agree", "change", "true");
 Assert.Equal("9 change agree true", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Newline_in_data_is_escaped_as_backslash_n()
 {
 var ev = new FormEvent(1, "memo", "change", "line1\nline2");
 Assert.Equal("1 change memo line1\\nline2", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Carriage_return_in_data_is_escaped_as_backslash_r()
 {
 var ev = new FormEvent(1, "memo", "change", "abc\rdef");
 Assert.Equal("1 change memo abc\\rdef", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Crlf_in_data_is_escaped_as_two_separate_escapes()
 {
 var ev = new FormEvent(1, "memo", "change", "abc\r\ndef");
 Assert.Equal("1 change memo abc\\r\\ndef", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Backslash_in_data_is_doubled()
 {
 var ev = new FormEvent(1, "path", "change", "a\\b");
 Assert.Equal("1 change path a\\\\b", FormEventFormatter.Format(ev));
 }

 [Fact]
 public void Empty_payload_does_not_emit_trailing_space()
 {
 var ev = new FormEvent(2, "btn", "click", string.Empty);
 string line = FormEventFormatter.Format(ev);
 Assert.Equal("2 click btn", line);
 // No trailing space when payload is empty
 Assert.False(line.EndsWith(" "));
 }

 [Fact]
 public void Format_never_contains_raw_newline()
 {
 var ev = new FormEvent(1, "memo", "change", "a\nb\rc\r\nd");
 string line = FormEventFormatter.Format(ev);
 Assert.DoesNotContain('\n', line);
 Assert.DoesNotContain('\r', line);
 }
 }
}
