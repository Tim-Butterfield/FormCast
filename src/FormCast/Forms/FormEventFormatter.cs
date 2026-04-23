// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/FormEventFormatter.cs
// ===========================
//
// Line formatter for FormEvent records. The FORMEVENTS streaming
// command writes one line per drained event to TCC's stdout via
// wwriteXP, and BTM scripts consume the stream with
// `do ev in /p formevents` plus `%@word[N, ,%ev]` to pull tokens out.
// The line shape is fixed by PLUGIN_DESIGN.md section 4.2:
//
// handle kind ctrl data
//
// Tokens are space-delimited; the data field is positionally last so
// any embedded spaces inside it are recovered by everything-after-
// word-3. Two normalizations make the format predictable for the
// BTM consumer:
//
// - The control id is replaced with "." for form-level events
// (e.g. "close") so the third token is always non-empty.
// - The data field has '\\', CR, and LF escaped so each event is
// guaranteed to occupy exactly one line. This may be revisited
// if a richer payload (e.g. JSON) is needed.
//
// When the data field is empty the trailing space is suppressed and
// the line ends after the control id, so a click event without a
// payload is "5 click go" rather than "5 click go " with a stray
// trailing blank.

using System.Globalization;
using System.Text;

namespace FormCast.Forms
{
 /// <summary>
 /// Pure formatter for <see cref="FormEvent"/> records. Kept
 /// separate from <see cref="FormEventQueue"/> and the FORMEVENTS
 /// dispatch method on <c>Plugin</c> so unit tests can pin the
 /// line shape without spinning up the GUI host or the queue.
 /// </summary>
    internal static class FormEventFormatter
    {
 /// <summary>
 /// Render <paramref name="ev"/> as a single line in the
 /// <c>handle kind ctrl data</c> shape consumed by
 /// <c>do ev in /p formevents</c>. Never returns a string
 /// containing CR or LF.
 /// </summary>
        public static string Format(FormEvent ev)
        {
            string ctrl = string.IsNullOrEmpty(ev.ControlId) ? "." : ev.ControlId;
            string data = Escape(ev.Value);
            string head = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2}",
                ev.FormHandle, ev.EventType, ctrl);
            return data.Length == 0 ? head : head + " " + data;
        }

 /// <summary>
 /// Escape characters that would break the one-event-per-line
 /// invariant. Backslash is escaped first so the doubled form
 /// can survive a round-trip through any unescaper that walks
 /// the string left to right.
 /// </summary>
        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) { return string.Empty; }

 // Fast path: nothing to escape.
            bool needs = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' || c == '\r' || c == '\n')
                {
                    needs = true;
                    break;
                }
            }
            if (!needs) { return s; }

            var sb = new StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
    }
}
