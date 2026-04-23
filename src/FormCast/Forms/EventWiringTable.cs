// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/EventWiringTable.cs
// =========================
//
// Registry-based event wiring for FormCast controls. Instead of a
// large switch statement in FormRealizer, each event is registered
// with a short name, the CLR event to wire, a control type filter,
// and a data extractor that converts EventArgs to a string payload.
//
// Adding a new event = one Register() call in the static constructor.
// The WireAll() method iterates the registry and wires matching
// events for a given control.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace FormCast.Forms
{
    /// <summary>
    /// Central registry of WinForms events that FormCast exposes to
    /// BTM scripts via FORMEVENTS. Each entry maps a short event name
    /// (e.g. "click", "keydown") to a CLR event on a specific control
    /// type, plus a data extractor that converts the EventArgs to a
    /// colon-delimited string payload.
    /// </summary>
    internal static class EventWiringTable
    {
        private static readonly List<EventEntry> _entries = new List<EventEntry>();

        static EventWiringTable()
        {
            // ============================================================
            // Common events (wired on every control)
            // ============================================================

            // Focus / blur
            On<Control>("focus", (c, q, fh, id) =>
                c.Enter += (s, e) => q.Enqueue(new FormEvent(fh, id, "focus", "")));
            On<Control>("blur", (c, q, fh, id) =>
                c.Leave += (s, e) => q.Enqueue(new FormEvent(fh, id, "blur", "")));

            // Double-click
            On<Control>("dblclick", (c, q, fh, id) =>
                c.DoubleClick += (s, e) => q.Enqueue(new FormEvent(fh, id, "dblclick", "")));

            // Mouse down/up with position and button
            On<Control>("mousedown", (c, q, fh, id) =>
                c.MouseDown += (s, e) => q.Enqueue(new FormEvent(fh, id, "mousedown",
                    Fmt(e.X) + ":" + Fmt(e.Y) + ":" + e.Button.ToString() + ":" + Fmt(e.Clicks))));
            On<Control>("mouseup", (c, q, fh, id) =>
                c.MouseUp += (s, e) => q.Enqueue(new FormEvent(fh, id, "mouseup",
                    Fmt(e.X) + ":" + Fmt(e.Y))));

            // Mouse enter/leave (hover detection)
            On<Control>("mouseenter", (c, q, fh, id) =>
                c.MouseEnter += (s, e) => q.Enqueue(new FormEvent(fh, id, "mouseenter", "")));
            On<Control>("mouseleave", (c, q, fh, id) =>
                c.MouseLeave += (s, e) => q.Enqueue(new FormEvent(fh, id, "mouseleave", "")));

            // Keyboard: keydown, keyup, keypress
            On<Control>("keydown", (c, q, fh, id) =>
                c.KeyDown += (s, e) => q.Enqueue(new FormEvent(fh, id, "keydown",
                    Fmt((int)e.KeyCode) + ":" + B(e.Shift) + ":" + B(e.Control) + ":" + B(e.Alt))));
            On<Control>("keyup", (c, q, fh, id) =>
                c.KeyUp += (s, e) => q.Enqueue(new FormEvent(fh, id, "keyup",
                    Fmt((int)e.KeyCode) + ":" + B(e.Shift) + ":" + B(e.Control) + ":" + B(e.Alt))));

            // Resize (useful on form-level and panels)
            On<Control>("resize", (c, q, fh, id) =>
                c.Resize += (s, e) => q.Enqueue(new FormEvent(fh, id, "resize",
                    Fmt(c.Width) + ":" + Fmt(c.Height))));

            // Drag and drop
            On<Control>("dragenter", (c, q, fh, id) =>
            {
                c.AllowDrop = true;
                c.DragEnter += (s, e) =>
                {
                    e.Effect = DragDropEffects.Copy;
                    string formats = string.Join("|", e.Data?.GetFormats() ?? Array.Empty<string>());
                    q.Enqueue(new FormEvent(fh, id, "dragenter", formats));
                };
            });
            On<Control>("dragdrop", (c, q, fh, id) =>
            {
                c.AllowDrop = true;
                c.DragDrop += (s, e) =>
                {
                    string data = "";
                    if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                    {
                        string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                        data = files is not null ? string.Join("|", files) : "";
                    }
                    else if (e.Data?.GetDataPresent(DataFormats.Text) == true)
                    {
                        data = e.Data.GetData(DataFormats.Text)?.ToString() ?? "";
                    }
                    q.Enqueue(new FormEvent(fh, id, "dragdrop",
                        Fmt(e.X) + ":" + Fmt(e.Y) + ":" + data));
                };
            });

            // ============================================================
            // Control-specific: click
            // ============================================================

            On<Button>("click", (c, q, fh, id) =>
                c.Click += (s, e) => q.Enqueue(new FormEvent(fh, id, "click", "")));
            On<LinkLabel>("click", (c, q, fh, id) =>
                c.LinkClicked += (s, e) => q.Enqueue(new FormEvent(fh, id, "click", "")));
            On<PictureBox>("click", (c, q, fh, id) =>
                c.Click += (s, e) => q.Enqueue(new FormEvent(fh, id, "click", "")));
            On<Panel>("click", (c, q, fh, id) =>
                c.Click += (s, e) => q.Enqueue(new FormEvent(fh, id, "click", "")));

            // ============================================================
            // Control-specific: change
            // ============================================================

            On<Controls.ToggleSwitch>("change", (c, q, fh, id) =>
                c.CheckedChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Checked ? "true" : "false")));
            // CheckBox before RadioButton (no inheritance issue, but
            // consistent with the old switch order).
            On<CheckBox>("change", (c, q, fh, id) =>
                c.CheckedChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Checked ? "true" : "false")));
            On<RadioButton>("change", (c, q, fh, id) =>
                c.CheckedChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Checked ? "true" : "false")));
            On<TextBox>("change", (c, q, fh, id) =>
                c.TextChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change", c.Text)));
            On<TextBox>("keypress", (c, q, fh, id) =>
                c.KeyPress += (s, e) => q.Enqueue(new FormEvent(fh, id, "keypress",
                    e.KeyChar.ToString())));
            On<Label>("change", (c, q, fh, id) =>
                c.TextChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change", c.Text)));
            On<ComboBox>("change", (c, q, fh, id) =>
                c.SelectedIndexChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.SelectedItem?.ToString() ?? "")));
            On<NumericUpDown>("change", (c, q, fh, id) =>
                c.ValueChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Value.ToString(CultureInfo.InvariantCulture))));
            On<DateTimePicker>("change", (c, q, fh, id) =>
                c.ValueChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Value.ToString("o", CultureInfo.InvariantCulture))));
            On<TrackBar>("change", (c, q, fh, id) =>
                c.ValueChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.Value.ToString(CultureInfo.InvariantCulture))));
            // CheckedListBox MUST come before ListBox (inherits).
            On<CheckedListBox>("change", (c, q, fh, id) =>
                c.ItemCheck += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    Fmt(e.Index) + ":" + (e.NewValue == CheckState.Checked ? "true" : "false"))));
            On<ListBox>("change", (c, q, fh, id) =>
                c.SelectedIndexChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    c.SelectedItem?.ToString() ?? "")));
            On<TreeView>("change", (c, q, fh, id) =>
                c.AfterSelect += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    e.Node?.Name ?? "")));
            On<MonthCalendar>("change", (c, q, fh, id) =>
                c.DateChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

            // ============================================================
            // Control-specific: scroll
            // ============================================================

            On<ScrollBar>("scroll", (c, q, fh, id) =>
                c.Scroll += (s, e) => q.Enqueue(new FormEvent(fh, id, "scroll",
                    Fmt(e.NewValue) + ":" + e.Type.ToString())));
            On<TrackBar>("scroll", (c, q, fh, id) =>
                c.Scroll += (s, e) => q.Enqueue(new FormEvent(fh, id, "scroll",
                    c.Value.ToString(CultureInfo.InvariantCulture))));

            // ============================================================
            // ListView-specific
            // ============================================================

            On<ListView>("columnclick", (c, q, fh, id) =>
                c.ColumnClick += (s, e) => q.Enqueue(new FormEvent(fh, id, "columnclick",
                    Fmt(e.Column))));
            On<ListView>("click", (c, q, fh, id) =>
                c.Click += (s, e) => q.Enqueue(new FormEvent(fh, id, "click", "")));

            // ============================================================
            // TreeView-specific
            // ============================================================

            On<TreeView>("beforeexpand", (c, q, fh, id) =>
                c.BeforeExpand += (s, e) => q.Enqueue(new FormEvent(fh, id, "beforeexpand",
                    e.Node?.Name ?? "")));
            On<TreeView>("afterexpand", (c, q, fh, id) =>
                c.AfterExpand += (s, e) => q.Enqueue(new FormEvent(fh, id, "afterexpand",
                    e.Node?.Name ?? "")));
            On<TreeView>("beforecollapse", (c, q, fh, id) =>
                c.BeforeCollapse += (s, e) => q.Enqueue(new FormEvent(fh, id, "beforecollapse",
                    e.Node?.Name ?? "")));
            On<TreeView>("aftercollapse", (c, q, fh, id) =>
                c.AfterCollapse += (s, e) => q.Enqueue(new FormEvent(fh, id, "aftercollapse",
                    e.Node?.Name ?? "")));

            // ============================================================
            // DataGridView-specific
            // ============================================================

            On<DataGridView>("cellclick", (c, q, fh, id) =>
                c.CellClick += (s, e) => q.Enqueue(new FormEvent(fh, id, "cellclick",
                    Fmt(e.RowIndex) + ":" + Fmt(e.ColumnIndex))));
            On<DataGridView>("change", (c, q, fh, id) =>
                c.CellValueChanged += (s, e) => q.Enqueue(new FormEvent(fh, id, "change",
                    Fmt(e.RowIndex) + ":" + Fmt(e.ColumnIndex))));
        }

        /// <summary>
        /// Return the event short-names that would be wired for
        /// a control of the given type. Used by the designer's
        /// PropertyGrid to show only relevant _bind.* entries.
        /// </summary>
        public static IReadOnlyList<string> GetEventsForType(Type controlType)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EventEntry entry in _entries)
            {
                if (entry.ControlType.IsAssignableFrom(controlType) &&
                    seen.Add(entry.EventName))
                {
                    result.Add(entry.EventName);
                }
            }
            return result;
        }

        /// <summary>
        /// Wire all matching events for a control. Called once per
        /// control during realization.
        /// </summary>
        public static void WireAll(Control control, int formHandle,
            string controlId, FormEventQueue queue)
        {
            // Track which event names have been wired to avoid
            // duplicates from inheritance (e.g. CheckedListBox
            // inheriting ListBox's "change" handler).
            var wired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EventEntry entry in _entries)
            {
                if (entry.ControlType.IsInstanceOfType(control) &&
                    wired.Add(entry.EventName))
                {
                    entry.Wire(control, queue, formHandle, controlId);
                }
            }
        }

        // ============================================================
        // Registration helpers
        // ============================================================

        private static void On<T>(string eventName,
            Action<T, FormEventQueue, int, string> wire) where T : Control
        {
            _entries.Add(new EventEntry(
                typeof(T),
                eventName,
                (ctrl, queue, fh, id) => wire((T)ctrl, queue, fh, id)));
        }

        // ============================================================
        // Formatting helpers (keep data payloads compact)
        // ============================================================

        private static string Fmt(int v) =>
            v.ToString(CultureInfo.InvariantCulture);

        private static string B(bool v) => v ? "1" : "0";

        // ============================================================
        // Entry record
        // ============================================================

        private sealed class EventEntry
        {
            public Type ControlType { get; }
            public string EventName { get; }
            public Action<Control, FormEventQueue, int, string> Wire { get; }

            public EventEntry(Type controlType, string eventName,
                Action<Control, FormEventQueue, int, string> wire)
            {
                ControlType = controlType;
                EventName = eventName;
                Wire = wire;
            }
        }
    }
}
