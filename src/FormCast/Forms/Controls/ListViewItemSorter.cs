// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/ListViewItemSorter.cs
// ====================================
//
// Type-aware comparer used by FormRealizer when wiring a
// LISTVIEW control's ListView.ListViewItemSorter. The comparer reads
// the active sort column index and direction from a parent
// ListViewState (set by header-click handlers) and dispatches to a
// per-column-type comparison function based on the column's declared
// type token from the descriptor's prop bag.
//
// Column types per PLUGIN_DESIGN.md section 6.12:
// text -> StringComparer.OrdinalIgnoreCase
// number -> double.TryParse, fall back to lexical
// date -> DateTime.TryParse, fall back to lexical
// size -> human-readable "1.2 KB" parsed to bytes
// icon -> column 0 only; sorted by image-list index (icon
// resolution is deferred; this comparer falls back to
// lexical so the column is at least sortable).
//
// The sorter is constructed once per ListView and stays installed.
// Header click events on the ListView mutate State.SortColumn /
// State.Ascending and call ListView.Sort() to re-trigger comparison.

using System;
using System.Collections;
using System.Globalization;
using System.Windows.Forms;

namespace FormCast.Forms.Controls
{
 /// <summary>
 /// Per-control sort state for a LISTVIEW. Mutated by the
 /// ListView header-click handler installed in
 /// <c>FormRealizer</c>; consulted by
 /// <see cref="ListViewItemSorter"/> on every Compare call.
 /// </summary>
    internal sealed class ListViewSortState
    {
 /// <summary>Active sort column index. -1 means "unsorted".</summary>
        public int SortColumn { get; set; } = -1;

 /// <summary>True for ascending, false for descending.</summary>
        public bool Ascending { get; set; } = true;

 /// <summary>
 /// Per-column type tokens, indexed by column position. Empty
 /// or unknown tokens fall back to <c>"text"</c>.
 /// </summary>
        public string[] ColumnTypes { get; set; } = Array.Empty<string>();
    }

 /// <summary>
 /// <see cref="IComparer"/> implementation that <c>ListView.Sort()</c>
 /// invokes for every pair of <see cref="ListViewItem"/> instances.
 /// Type-aware: dispatches to a per-column comparison based on the
 /// declared column type token from the descriptor's prop bag.
 /// </summary>
    internal sealed class ListViewItemSorter : IComparer
    {
        private readonly ListViewSortState _state;

        public ListViewItemSorter(ListViewSortState state)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

 /// <inheritdoc />
        public int Compare(object? x, object? y)
        {
            if (x is not ListViewItem a || y is not ListViewItem b) { return 0; }
            int col = _state.SortColumn;
            if (col < 0) { return 0; }

            string sa = col < a.SubItems.Count ? a.SubItems[col].Text : string.Empty;
            string sb = col < b.SubItems.Count ? b.SubItems[col].Text : string.Empty;

            string type = col < _state.ColumnTypes.Length
                ? (_state.ColumnTypes[col] ?? "text")
                : "text";

            int result = CompareByType(sa, sb, type);
            return _state.Ascending ? result : -result;
        }

 // -----------------------------------------------------------------
 // Per-type comparisons. Each one degrades to lexical on parse
 // failure so a column with mixed-format cells stays sortable
 // even if some rows do not parse cleanly.
 // -----------------------------------------------------------------

        internal static int CompareByType(string a, string b, string type)
        {
            switch ((type ?? "text").ToLowerInvariant())
            {
                case "number":
                    return CompareNumeric(a, b);
                case "date":
                    return CompareDate(a, b);
                case "size":
                    return CompareSize(a, b);
                case "icon":
 // Full icon-index sort is deferred; fall through to
 // lexical so the column is at least sortable.
 // Image-list index compare can be added when the
 // icon resolution path lands.
                    return CompareLexical(a, b);
                case "text":
                default:
                    return CompareLexical(a, b);
            }
        }

        private static int CompareLexical(string a, string b)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(a, b);
        }

        private static int CompareNumeric(string a, string b)
        {
            bool ok1 = double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double da);
            bool ok2 = double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double db);
            if (ok1 && ok2) { return da.CompareTo(db); }
            if (ok1) { return -1; }
            if (ok2) { return 1; }
            return CompareLexical(a, b);
        }

        private static int CompareDate(string a, string b)
        {
            bool ok1 = DateTime.TryParse(a, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime da);
            bool ok2 = DateTime.TryParse(b, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime db);
            if (ok1 && ok2) { return da.CompareTo(db); }
            if (ok1) { return -1; }
            if (ok2) { return 1; }
            return CompareLexical(a, b);
        }

 /// <summary>
 /// Parse a human-readable size string ("1.2 KB", "5 MB",
 /// "120", "1.5GB") into bytes. Returns -1 on parse failure.
 /// Recognized suffixes: B, K, KB, M, MB, G, GB, T, TB. Case-
 /// insensitive. Whitespace between number and suffix is
 /// optional.
 /// </summary>
        internal static long ParseSizeBytes(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) { return -1; }
            string trimmed = s.Trim();

 // Find the split between numeric and suffix.
            int i = 0;
            while (i < trimmed.Length &&
                   (char.IsDigit(trimmed[i]) || trimmed[i] == '.' || trimmed[i] == ',' ||
                    trimmed[i] == '-' || trimmed[i] == '+'))
            {
                i++;
            }
            if (i == 0) { return -1; }
            string numPart = trimmed.Substring(0, i);
            string suffix = trimmed.Substring(i).TrimStart().ToUpperInvariant();

            if (!double.TryParse(numPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
            {
                return -1;
            }

            double mul;
            switch (suffix)
            {
                case "":
                case "B":
                    mul = 1; break;
                case "K":
                case "KB":
                    mul = 1024d; break;
                case "M":
                case "MB":
                    mul = 1024d * 1024d; break;
                case "G":
                case "GB":
                    mul = 1024d * 1024d * 1024d; break;
                case "T":
                case "TB":
                    mul = 1024d * 1024d * 1024d * 1024d; break;
                default:
                    return -1;
            }

            double bytes = n * mul;
            if (bytes < 0 || bytes > long.MaxValue) { return -1; }
            return (long)bytes;
        }

        private static int CompareSize(string a, string b)
        {
            long ba = ParseSizeBytes(a);
            long bb = ParseSizeBytes(b);
            if (ba >= 0 && bb >= 0) { return ba.CompareTo(bb); }
            if (ba >= 0) { return -1; }
            if (bb >= 0) { return 1; }
            return CompareLexical(a, b);
        }
    }
}
