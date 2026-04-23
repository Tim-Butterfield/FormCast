// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/RichMemoBuilder.cs
// =================================
//
// Build a RICHMEMO control. RICHMEMO is a WPF
// System.Windows.Controls.RichTextBox embedded in a WinForms form via
// WindowsFormsIntegration.ElementHost. The WPF surface is what gives
// us inline color/style runs and regex-based syntax highlighting; the
// ElementHost is what lets it sit alongside Button/TextBox/ListView in
// a normal WinForms form without needing a separate WPF Window.
//
// Three operations are exposed via @FORMSET:
//
// appendcolor : "text|color"
// Append `text` to the document with `color` as the foreground
// brush. `color` is parsed by System.Windows.Media.BrushConverter
// so any WPF color name ("Red", "DarkSlateBlue") or #RRGGBB
// hex string works.
//
// appendstyle : "text|style"
// Append `text` to the document with `style` applied. `style`
// is one of `bold`, `italic`, `underline` or any combination
// joined by '+' (e.g. `bold+italic`).
//
// loadrules : "regex|color,regex|color,..."
// Apply regex-based syntax highlighting to the entire current
// document. Each rule is `regex|color` (color parsed the same
// way as appendcolor); rules are evaluated left to right and
// overlapping matches are NOT deduplicated -- the last rule
// wins for any given character position because WPF's
// TextRange property setter is last-write-wins.
//
// Forced shutdown dispose order
// -----------------------------
// ElementHost wraps a WPF visual tree. If the WinForms host disposes
// the ElementHost while the WPF tree is still attached, the dispose
// races against WPF's dispatcher teardown and can crash the host
// process during plugin /u. The contract from PLUGIN_DESIGN.md
// section 4.6 is:
//
// 1. ElementHost.Child = null
// 2. ElementHost.Dispose()
//
// FormRealizer.Destroy walks every control tree on the GUI thread and
// invokes IDisposable.Dispose. To enforce the order we hook the
// ElementHost.HandleDestroyed event in this builder so the child is
// nulled out exactly once before the host destroys its handle. This
// runs on the GUI thread inside the forced-shutdown sweep, so there
// is no cross-thread risk.

using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Forms.Integration;
using System.Windows.Media;

namespace FormCast.Forms.Controls
{
 /// <summary>
 /// Builds a RICHMEMO control: a WPF
 /// <see cref="System.Windows.Controls.RichTextBox"/> hosted in an
 /// <see cref="ElementHost"/>. The host is what
 /// <see cref="FormRealizer"/> adds to the form's
 /// <c>Form.Controls</c> collection; the actual rich text live on
 /// the WPF child accessed via <see cref="GetEditor(ElementHost)"/>.
 /// </summary>
    internal static class RichMemoBuilder
    {
 /// <summary>
 /// Construct a populated <see cref="ElementHost"/> for the
 /// given descriptor. Initial text is set from
 /// <see cref="ControlDescriptor.Text"/>; the <c>readonly</c>
 /// prop bag flag flips
 /// <see cref="System.Windows.Controls.Primitives.TextBoxBase.IsReadOnly"/>.
 /// </summary>
        public static ElementHost Build(ControlDescriptor desc)
        {
            var editor = new System.Windows.Controls.RichTextBox
            {
                AcceptsReturn = true,
                AcceptsTab = true,
                IsReadOnly = ParseBoolFlag(GetProp(desc, "readonly")),
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
            };

 // Initial document content. Use a single paragraph so the
 // append operations have a stable insertion point at the
 // end of document.
            editor.Document = new FlowDocument(new Paragraph(new Run(desc.Text ?? string.Empty)));

            var host = new ElementHost
            {
                Child = editor,
            };

 // Forced-shutdown dispose-order safety net. We null the
 // ElementHost.Child before the host's HWND is destroyed
 // so the WPF tree detaches in the right order. Without
 // this, plugin /u during teardown of a form containing a
 // RICHMEMO can crash the host process.
            host.HandleDestroyed += (s, e) =>
            {
                if (host.Child is not null)
                {
                    host.Child = null;
                }
            };

            return host;
        }

 /// <summary>
 /// Resolve the WPF <see cref="System.Windows.Controls.RichTextBox"/>
 /// hosted inside an <see cref="ElementHost"/> built by
 /// <see cref="Build"/>. Returns <c>null</c> if the host's
 /// <see cref="ElementHost.Child"/> is missing or has been
 /// replaced.
 /// </summary>
        public static System.Windows.Controls.RichTextBox? GetEditor(ElementHost host)
        {
            return host.Child as System.Windows.Controls.RichTextBox;
        }

 // -----------------------------------------------------------------
 // Operations: must be called on the GUI thread (the same
 // thread that owns the WPF dispatcher).
 // -----------------------------------------------------------------

 /// <summary>
 /// Append <paramref name="text"/> to the end of the document
 /// with the given foreground <paramref name="colorName"/>.
 /// Returns <c>true</c> on success, <c>false</c> if the color
 /// name does not parse.
 /// </summary>
        public static bool AppendColor(ElementHost host, string text, string colorName)
        {
            System.Windows.Controls.RichTextBox? editor = GetEditor(host);
            if (editor is null) { return false; }

            Brush? brush = ParseBrush(colorName);
            if (brush is null) { return false; }

            var run = new Run(text ?? string.Empty)
            {
                Foreground = brush,
            };
            AppendInline(editor, run);
            return true;
        }

 /// <summary>
 /// Append <paramref name="text"/> with the given style. Style
 /// is one of <c>bold</c>, <c>italic</c>, <c>underline</c> or
 /// any combination joined by <c>'+'</c>.
 /// </summary>
        public static bool AppendStyle(ElementHost host, string text, string style)
        {
            System.Windows.Controls.RichTextBox? editor = GetEditor(host);
            if (editor is null) { return false; }

            var run = new Run(text ?? string.Empty);
            string s = (style ?? string.Empty).ToLowerInvariant();
            if (s.Contains("bold")) { run.FontWeight = FontWeights.Bold; }
            if (s.Contains("italic")) { run.FontStyle = FontStyles.Italic; }
            if (s.Contains("underline"))
            {
 // Underline is a TextDecoration, not a Run property.
 // Wrap in a Span carrying the decoration so the run's
 // text picks it up at render time.
                var span = new Span(run);
                span.TextDecorations = TextDecorations.Underline;
                AppendInline(editor, span);
                return true;
            }

            AppendInline(editor, run);
            return true;
        }

 /// <summary>
 /// Apply a set of regex-based highlighting rules to the
 /// current document content. Rules are
 /// <c>regex|color,regex|color,...</c>. Each match is rewritten
 /// in place by selecting the matched character range and
 /// setting its foreground brush. Returns the number of rules
 /// successfully applied (rules whose regex or color do not
 /// parse are silently skipped).
 /// </summary>
        public static int LoadRules(ElementHost host, string rulesSpec)
        {
            System.Windows.Controls.RichTextBox? editor = GetEditor(host);
            if (editor is null || string.IsNullOrEmpty(rulesSpec)) { return 0; }

 // Walk the entire document text once. For each rule, find
 // every match and apply the brush to the corresponding
 // TextRange. Building TextPointers from offsets is the
 // load-bearing trick: WPF's FlowDocument exposes
 // TextPointer arithmetic via GetPositionAtOffset which
 // counts symbols (not characters), so we walk the inline
 // tree to map character offsets back to TextPointers.

            string fullText = new TextRange(
                editor.Document.ContentStart,
                editor.Document.ContentEnd).Text;

            int applied = 0;
            string[] rules = rulesSpec.Split(',');
            foreach (string rule in rules)
            {
                int bar = rule.IndexOf('|');
                if (bar < 0) { continue; }
                string pattern = rule.Substring(0, bar);
                string colorName = rule.Substring(bar + 1);

                Brush? brush = ParseBrush(colorName);
                if (brush is null) { continue; }

                Regex re;
                try { re = new Regex(pattern); }
                catch (ArgumentException) { continue; }

                bool any = false;
                foreach (Match m in re.Matches(fullText))
                {
                    if (m.Length == 0) { continue; }
                    TextPointer? start = GetPositionAtCharOffset(
                        editor.Document.ContentStart, m.Index);
                    TextPointer? end = GetPositionAtCharOffset(
                        editor.Document.ContentStart, m.Index + m.Length);
                    if (start is null || end is null) { continue; }

                    var range = new TextRange(start, end);
                    range.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    any = true;
                }
                if (any) { applied++; }
            }
            return applied;
        }

 // -----------------------------------------------------------------
 // Internal helpers
 // -----------------------------------------------------------------

        private static void AppendInline(
            System.Windows.Controls.RichTextBox editor, Inline inline)
        {
 // Append to the last paragraph if there is one, otherwise
 // create one. The Build path always seeds with a single
 // paragraph so the lookup typically succeeds on the first
 // try.
            if (editor.Document.Blocks.LastBlock is Paragraph p)
            {
                p.Inlines.Add(inline);
            }
            else
            {
                var newPara = new Paragraph(inline);
                editor.Document.Blocks.Add(newPara);
            }
        }

 /// <summary>
 /// Map a 0-based character offset (counted across the entire
 /// document text per <see cref="TextRange.Text"/>) to a
 /// <see cref="TextPointer"/>. Walks symbols using
 /// <see cref="TextPointer.GetNextContextPosition"/> and counts
 /// only character symbols (not start/end-of-element ones).
 /// Returns <c>null</c> when the offset is past the end of the
 /// document.
 /// </summary>
        private static TextPointer? GetPositionAtCharOffset(TextPointer start, int charOffset)
        {
            if (charOffset < 0) { return null; }
            TextPointer? pos = start;
            int counted = 0;
            while (pos is not null)
            {
                if (counted == charOffset) { return pos; }
                if (pos.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    int run = pos.GetTextRunLength(LogicalDirection.Forward);
                    int needed = charOffset - counted;
                    if (needed <= run)
                    {
                        return pos.GetPositionAtOffset(needed, LogicalDirection.Forward);
                    }
                    counted += run;
                    pos = pos.GetPositionAtOffset(run, LogicalDirection.Forward);
                }
                else
                {
                    pos = pos.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return null;
        }

        private static Brush? ParseBrush(string? colorName)
        {
            if (string.IsNullOrWhiteSpace(colorName)) { return null; }
            try
            {
                object? converted = new BrushConverter().ConvertFromString(colorName);
                return converted as Brush;
            }
            catch (FormatException) { return null; }
            catch (NotSupportedException) { return null; }
        }

        private static string? GetProp(ControlDescriptor desc, string key)
        {
            return desc.Properties.TryGetValue(key, out string? v) ? v : null;
        }

        private static bool ParseBoolFlag(string? value)
        {
            if (string.IsNullOrEmpty(value)) { return false; }
            switch (value!.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

 /// <summary>
 /// Read the entire current text content of the editor as a
 /// plain string, with no styling. Used by FORMGET text on a
 /// realized RICHMEMO so BTM scripts can pull the document
 /// contents back into a SET variable.
 /// </summary>
        public static string GetPlainText(ElementHost host)
        {
            System.Windows.Controls.RichTextBox? editor = GetEditor(host);
            if (editor is null) { return string.Empty; }
            string text = new TextRange(
                editor.Document.ContentStart,
                editor.Document.ContentEnd).Text;
 // WPF appends a trailing \r\n to the document text; trim
 // it so the round-trip with FORMSET text matches the
 // descriptor's stored value.
            return text.TrimEnd('\r', '\n');
        }

 /// <summary>
 /// Reset the editor's document to a single paragraph holding
 /// only <paramref name="newText"/>. Used by FORMSET text on a
 /// realized RICHMEMO so BTM scripts can replace the entire
 /// content rather than appending to it.
 /// </summary>
        public static void SetPlainText(ElementHost host, string newText)
        {
            System.Windows.Controls.RichTextBox? editor = GetEditor(host);
            if (editor is null) { return; }
            editor.Document = new FlowDocument(new Paragraph(new Run(newText ?? string.Empty)));
        }
    }
}
