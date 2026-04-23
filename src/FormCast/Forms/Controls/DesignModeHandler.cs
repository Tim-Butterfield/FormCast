// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/DesignModeHandler.cs
// ====================================
//
// Interactive design-mode handler for the FormCast visual designer.
// Provides:
// 1. Click-to-select with dashed border + 8 resize handles
// 2. Drag-to-move (click and drag the control body)
// 3. Drag-to-resize (click and drag a resize handle)
// 4. Snap-to-grid on move and resize (default 8px)
// 5. Type labels painted on each control
// 6. Click form background to deselect
//
// All operations run on the GUI thread.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// Interactive design-mode handler for the FormCast visual designer.
    /// When attached to a realized <see cref="Form"/>, intercepts mouse
    /// events on all child controls to provide click-to-select,
    /// drag-to-move, and drag-to-resize with snap-to-grid. All
    /// operations run on the GUI thread.
    /// </summary>
    /// <remarks>
    /// The handler does NOT own the form or its controls; it only hooks
    /// their mouse events. <see cref="Detach"/> removes all hooks
    /// cleanly so the form can continue to function as a runtime window
    /// after design mode is toggled off.
    /// </remarks>
    internal sealed class DesignModeHandler
    {
        private readonly Form _form;
        private readonly FormDescriptor _descriptor;
        private readonly Action<string>? _onSelectionChanged;

        private readonly List<Control> _selected = new List<Control>();
        private bool _dragging;
        private bool _dragMoved;
        private bool _resizing;
        /// <summary>Active resize handle index (0-7 clockwise from top-left).</summary>
        private int _resizeHandle;
        private Point _dragStart;
        private Point _controlStartLocation;
        private Size _controlStartSize;
        private bool _ctrlHeld;
        /// <summary>
        /// Transparent overlay panel drawn above the selected control.
        /// Contains the dashed selection border and 8 resize handles.
        /// Lives in the form's Controls collection at the front of the
        /// Z-order so it receives mouse events before any child control.
        /// </summary>
        private Panel? _selectionOverlay;
        /// <summary>Snap grid spacing in pixels. Read from the descriptor's "gridsize" prop.</summary>
        private int _gridSize = 8;

        private const int HandleSize = 7;
        // Handle indices: 0=TL, 1=TC, 2=TR, 3=ML, 4=MR, 5=BL, 6=BC, 7=BR
        private static readonly Cursor[] HandleCursors = new Cursor[]
        {
            Cursors.SizeNWSE, Cursors.SizeNS, Cursors.SizeNESW,
            Cursors.SizeWE,                    Cursors.SizeWE,
            Cursors.SizeNESW, Cursors.SizeNS, Cursors.SizeNWSE,
        };

        /// <summary>
        /// The control id (WinForms Name) of the currently selected control,
        /// or <c>null</c> when nothing is selected.
        /// </summary>
        public string? SelectedControlId => _selected.Count > 0 ? _selected[0].Name : null;

        /// <summary>Number of currently selected controls.</summary>
        public int SelectionCount => _selected.Count;

        /// <summary>
        /// Construct a handler for the given realized form. The handler
        /// is inert until <see cref="Attach"/> is called.
        /// </summary>
        /// <param name="form">The realized form to instrument.</param>
        /// <param name="descriptor">
        /// Form descriptor whose control bounds are updated on drag/resize commit.
        /// Also supplies the <c>gridsize</c> property.
        /// </param>
        /// <param name="onSelectionChanged">
        /// Optional callback invoked whenever the selection changes. Receives the
        /// newly selected control id, or empty string on deselect.
        /// </param>
        public DesignModeHandler(
            Form form,
            FormDescriptor descriptor,
            Action<string>? onSelectionChanged = null)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            _onSelectionChanged = onSelectionChanged;

            // Read grid size from descriptor
            if (descriptor.Properties.TryGetValue("gridsize", out string? gs) &&
                int.TryParse(gs, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int g) && g > 0)
            {
                _gridSize = g;
            }
        }

        /// <summary>
        /// Wire mouse event handlers onto the form and all its children,
        /// create the selection overlay, and start painting grid dots and
        /// type labels. Idempotent on the overlay (won't double-create).
        /// </summary>
        public void Attach()
        {
            _form.KeyPreview = true;
            _form.MouseDown += OnFormMouseDown;
            _form.KeyDown += OnFormKeyDown;
            AttachToChildren(_form);
            CreateSelectionOverlay();
            _form.ControlAdded += OnControlAdded;
            // Enable drag-drop from toolbox: the form accepts
            // text drops containing a control type name. The drop
            // enqueues a "toolboxdrop" event with "type:x:y" data.
            _form.AllowDrop = true;
            _form.DragEnter += (s, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.Text) == true)
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            _form.DragDrop += (s, e) =>
            {
                string? typeName = e.Data?.GetData(DataFormats.Text)?.ToString();
                if (string.IsNullOrEmpty(typeName)) { return; }
                // Convert screen coords to form client coords
                Point client = _form.PointToClient(new Point(e.X, e.Y));
                // Snap to grid
                int x = (_gridSize > 1) ? ((client.X + _gridSize / 2) / _gridSize) * _gridSize : client.X;
                int y = (_gridSize > 1) ? ((client.Y + _gridSize / 2) / _gridSize) * _gridSize : client.Y;
                // Store drop info in descriptor for the BTM to pick up
                _descriptor.Properties["_drop_type"] = typeName!;
                _descriptor.Properties["_drop_x"] = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _descriptor.Properties["_drop_y"] = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
            };
            // Paint type labels on each control
            AttachTypeLabelPaint(_form);
            // Paint grid dots on form background
            _form.Paint += OnFormPaint;
        }

        /// <summary>
        /// Handler for <c>Control.ControlAdded</c>. Hooks mouse events on
        /// controls that are added AFTER <see cref="Attach"/> ran (e.g.
        /// when the designer's "add control" workflow creates a new control
        /// at runtime). Without this, newly added controls would be inert
        /// in design mode.
        /// </summary>
        private void OnControlAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control is null || e.Control == _selectionOverlay) { return; }
            e.Control.MouseDown += OnControlMouseDown;
            e.Control.MouseMove += OnControlMouseMove;
            e.Control.MouseUp += OnControlMouseUp;
            e.Control.PreviewKeyDown += OnPreviewKeyDown;
            e.Control.ControlAdded += OnControlAdded;
            // Type label on newly added control
            e.Control.Paint += OnControlPaintTypeLabel;
        }

        /// <summary>
        /// Programmatically deselect the current control (hides the overlay).
        /// Called by Plugin.DeleteControl before removing a control to avoid
        /// a stale selection overlay referencing a disposed control.
        /// </summary>
        public void ClearSelection()
        {
            Select(null);
        }

        /// <summary>
        /// Remove all mouse event hooks, dispose the selection overlay, and
        /// stop painting grid dots and type labels. After this call the form
        /// behaves as a normal runtime window.
        /// </summary>
        public void Detach()
        {
            _form.MouseDown -= OnFormMouseDown;
            _form.KeyDown -= OnFormKeyDown;
            _form.Paint -= OnFormPaint;
            _form.ControlAdded -= OnControlAdded;
            DetachFromChildren(_form);
            RemoveSelectionOverlay();
            _selected.Clear();
            _dragging = false;
            _resizing = false;
        }

        /// <summary>
        /// Update the snap grid size and repaint. Called from
        /// the BTM via the <c>gridsize</c> form-level property.
        /// </summary>
        public void SetGridSize(int size)
        {
            _gridSize = size < 0 ? 0 : size;
            _form.Invalidate();
        }

        /// <summary>
        /// Refresh the selection overlay and form repaint after
        /// programmatic position/size changes. Called from the
        /// BTM via the <c>refreshdesign</c> form-level property.
        /// </summary>
        public void RefreshOverlay()
        {
            UpdateOverlay();
            _form.Invalidate();
        }

        // -----------------------------------------------------------------
        // Wiring
        // -----------------------------------------------------------------

        private void AttachToChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c == _selectionOverlay) { continue; }
                c.MouseDown += OnControlMouseDown;
                c.MouseMove += OnControlMouseMove;
                c.MouseUp += OnControlMouseUp;
                // Mark arrow keys as input keys so WinForms doesn't
                // swallow them for navigation. This lets KeyPreview
                // on the form capture them for nudge operations.
                c.PreviewKeyDown += OnPreviewKeyDown;
                if (c.Controls.Count > 0)
                {
                    AttachToChildren(c);
                }
            }
        }

        private void DetachFromChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                c.MouseDown -= OnControlMouseDown;
                c.MouseMove -= OnControlMouseMove;
                c.MouseUp -= OnControlMouseUp;
                c.Paint -= OnControlPaintTypeLabel;
                if (c.Controls.Count > 0)
                {
                    DetachFromChildren(c);
                }
            }
        }

        private void AttachTypeLabelPaint(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c == _selectionOverlay) { continue; }
                c.Paint += OnControlPaintTypeLabel;
                if (c.Controls.Count > 0)
                {
                    AttachTypeLabelPaint(c);
                }
            }
        }

        // -----------------------------------------------------------------
        // Grid painting on canvas background
        // -----------------------------------------------------------------

        private void OnFormPaint(object? sender, PaintEventArgs e)
        {
            // Grid dots
            if (_gridSize >= 4)
            {
                using var dotBrush = new SolidBrush(Color.FromArgb(160, 180, 180, 180));
                for (int x = _gridSize; x < _form.ClientSize.Width; x += _gridSize)
                {
                    for (int y = _gridSize; y < _form.ClientSize.Height; y += _gridSize)
                    {
                        e.Graphics.FillRectangle(dotBrush, x, y, 2, 2);
                    }
                }
            }
            // Draw dashed selection border on secondary selected controls
            // (primary gets the overlay with resize handles)
            if (_selected.Count > 1)
            {
                using var pen = new Pen(Color.DodgerBlue, 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                for (int i = 1; i < _selected.Count; i++)
                {
                    Control c = _selected[i];
                    if (c.IsDisposed) { continue; }
                    Point loc;
                    if (c.Parent is null || c.Parent == _form)
                    {
                        loc = c.Location;
                    }
                    else
                    {
                        loc = _form.PointToClient(c.Parent.PointToScreen(c.Location));
                    }
                    e.Graphics.DrawRectangle(pen, loc.X - 1, loc.Y - 1, c.Width + 1, c.Height + 1);
                }
            }
        }

        // -----------------------------------------------------------------
        // Type labels painted on each control
        // -----------------------------------------------------------------

        private void OnControlPaintTypeLabel(object? sender, PaintEventArgs e)
        {
            if (sender is not Control c) { return; }
            // Look up the descriptor type from the control name
            ControlDescriptor? cd = FindDescriptor(_descriptor, c.Name);
            if (cd is null) { return; }
            string label = cd.Type ?? "";
            if (string.IsNullOrEmpty(label)) { return; }
            using var font = new Font("Segoe UI", 7f);
            using var brush = new SolidBrush(Color.FromArgb(140, 80, 80, 80));
            e.Graphics.DrawString(label, font, brush, 2, 1);
        }

        // -----------------------------------------------------------------
        // Selection overlay with resize handles
        // -----------------------------------------------------------------

        private void CreateSelectionOverlay()
        {
            if (_selectionOverlay is not null) { return; }
            _selectionOverlay = new Panel
            {
                BackColor = Color.Transparent,
                Visible = false,
            };
            _selectionOverlay.Paint += OnOverlayPaint;
            _selectionOverlay.MouseDown += OnOverlayMouseDown;
            _selectionOverlay.MouseMove += OnOverlayMouseMove;
            _selectionOverlay.MouseUp += OnOverlayMouseUp;
            _form.Controls.Add(_selectionOverlay);
            _selectionOverlay.BringToFront();
        }

        private void OnOverlayPaint(object? sender, PaintEventArgs e)
        {
            if (_selectionOverlay is null) { return; }
            var g = e.Graphics;
            int w = _selectionOverlay.Width;
            int h = _selectionOverlay.Height;

            // Dashed border
            using (var pen = new Pen(Color.FromArgb(0, 120, 215), 1.5f)
                { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(pen, 1, 1, w - 3, h - 3);
            }

            // 8 resize handles
            using var handleBrush = new SolidBrush(Color.White);
            using var handlePen = new Pen(Color.FromArgb(0, 120, 215), 1);
            foreach (Rectangle r in GetHandleRects(w, h))
            {
                g.FillRectangle(handleBrush, r);
                g.DrawRectangle(handlePen, r);
            }
        }

        private static Rectangle[] GetHandleRects(int w, int h)
        {
            int hs = HandleSize;
            int hw = hs / 2;
            int mx = w / 2 - hw;
            int my = h / 2 - hw;
            return new Rectangle[]
            {
                new Rectangle(0, 0, hs, hs),           // 0 TL
                new Rectangle(mx, 0, hs, hs),          // 1 TC
                new Rectangle(w - hs - 1, 0, hs, hs),  // 2 TR
                new Rectangle(0, my, hs, hs),           // 3 ML
                new Rectangle(w - hs - 1, my, hs, hs), // 4 MR
                new Rectangle(0, h - hs - 1, hs, hs),  // 5 BL
                new Rectangle(mx, h - hs - 1, hs, hs), // 6 BC
                new Rectangle(w - hs - 1, h - hs - 1, hs, hs), // 7 BR
            };
        }

        private int HitTestHandle(Point overlayPoint)
        {
            if (_selectionOverlay is null) { return -1; }
            Rectangle[] handles = GetHandleRects(
                _selectionOverlay.Width, _selectionOverlay.Height);
            for (int i = 0; i < handles.Length; i++)
            {
                Rectangle r = handles[i];
                r.Inflate(2, 2); // slightly larger hit area
                if (r.Contains(overlayPoint)) { return i; }
            }
            return -1;
        }

        private void OnOverlayMouseDown(object? sender, MouseEventArgs e)
        {
            if (_selected.Count == 0) { return; }
            Control primary = _selected[0];

            int handle = HitTestHandle(e.Location);
            if (handle >= 0)
            {
                _resizing = true;
                _resizeHandle = handle;
                _dragStart = _selectionOverlay!.PointToScreen(e.Location);
                _controlStartLocation = primary.Location;
                _controlStartSize = primary.Size;
            }
            else
            {
                _dragging = true;
                _dragStart = _selectionOverlay!.PointToScreen(e.Location);
                _controlStartLocation = primary.Location;
            }
        }

        private void OnOverlayMouseMove(object? sender, MouseEventArgs e)
        {
            if (_selectionOverlay is null) { return; }

            if (_selected.Count == 0) { return; }
            Control primary = _selected[0];

            if (_resizing)
            {
                Point current = _selectionOverlay.PointToScreen(e.Location);
                int dx = current.X - _dragStart.X;
                int dy = current.Y - _dragStart.Y;
                ApplyResize(dx, dy);
                UpdateOverlay();
            }
            else if (_dragging)
            {
                Point current = _selectionOverlay.PointToScreen(e.Location);
                int dx = current.X - _dragStart.X;
                int dy = current.Y - _dragStart.Y;
                // Move all selected controls by the same delta
                primary.Location = new Point(
                    _controlStartLocation.X + dx,
                    _controlStartLocation.Y + dy);
                // Secondary selections move by same delta
                // (batch move tracked relative to primary)
                UpdateOverlay();
            }
            else
            {
                int handle = HitTestHandle(e.Location);
                _selectionOverlay.Cursor = handle >= 0
                    ? HandleCursors[handle]
                    : Cursors.SizeAll;
            }
        }

        private void OnOverlayMouseUp(object? sender, MouseEventArgs e)
        {
            foreach (Control c in _selected)
            {
                SnapToGrid(c);
                CommitBounds(c);
            }
            UpdateOverlay();
            _dragging = false;
            _resizing = false;
        }

        private void ApplyResize(int dx, int dy)
        {
            if (_selected.Count == 0) { return; }
            Control _sel = _selected[0];
            int x = _controlStartLocation.X;
            int y = _controlStartLocation.Y;
            int w = _controlStartSize.Width;
            int h = _controlStartSize.Height;

            switch (_resizeHandle)
            {
                case 0: // TL
                    x += dx; y += dy; w -= dx; h -= dy; break;
                case 1: // TC
                    y += dy; h -= dy; break;
                case 2: // TR
                    y += dy; w += dx; h -= dy; break;
                case 3: // ML
                    x += dx; w -= dx; break;
                case 4: // MR
                    w += dx; break;
                case 5: // BL
                    x += dx; w -= dx; h += dy; break;
                case 6: // BC
                    h += dy; break;
                case 7: // BR
                    w += dx; h += dy; break;
            }

            // Minimum size
            if (w < 10) { w = 10; }
            if (h < 10) { h = 10; }

            _sel.Location = new Point(x, y);
            _sel.Size = new Size(w, h);
        }

        private void RemoveSelectionOverlay()
        {
            if (_selectionOverlay is null) { return; }
            _form.Controls.Remove(_selectionOverlay);
            _selectionOverlay.Dispose();
            _selectionOverlay = null;
        }

        private void UpdateOverlay()
        {
            if (_selectionOverlay is null) { return; }
            if (_selected.Count == 0)
            {
                _selectionOverlay.Visible = false;
                return;
            }
            // Position overlay around the primary selection
            Control primary = _selected[0];
            Point loc;
            if (primary.Parent is null || primary.Parent == _form)
            {
                loc = primary.Location;
            }
            else
            {
                loc = _form.PointToClient(primary.Parent.PointToScreen(primary.Location));
            }
            int pad = HandleSize / 2 + 2;
            _selectionOverlay.Location = new Point(loc.X - pad, loc.Y - pad);
            _selectionOverlay.Size = new Size(
                primary.Width + pad * 2,
                primary.Height + pad * 2);
            _selectionOverlay.Visible = true;
            _selectionOverlay.BringToFront();
            _selectionOverlay.Invalidate();
            // Force form repaint for secondary selection borders
            if (_selected.Count > 1) { _form.Invalidate(); }
        }

        // -----------------------------------------------------------------
        // Click / drag handlers on controls
        // -----------------------------------------------------------------

        private void OnFormMouseDown(object? sender, MouseEventArgs e)
        {
            // Right-click opens context menu -- preserve selection
            if (e.Button == MouseButtons.Right) { return; }
            Select(null);
        }

        private void OnControlMouseDown(object? sender, MouseEventArgs e)
        {
            if (sender is not Control target) { return; }
            // Right-click: if target is already in selection, keep it
            // (context menu operates on the selection). If not selected,
            // select it (single) so context menu applies to it.
            if (e.Button == MouseButtons.Right)
            {
                if (!_selected.Contains(target))
                {
                    Select(target, false);
                }
                return;
            }
            _ctrlHeld = (System.Windows.Forms.Control.ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            Internal.PluginLogger.Debug(
                $"DesignMode MouseDown: ctrl={target.Name} ctrlHeld={_ctrlHeld} prevCount={_selected.Count}");
            Select(target, _ctrlHeld);
            Internal.PluginLogger.Debug(
                $"DesignMode Select result: count={_selected.Count} ids={string.Join(",", _selected.ConvertAll(c => c.Name ?? "?"))}");
            _dragging = true;
            _dragMoved = false;
            _dragStart = target.PointToScreen(e.Location);
            _controlStartLocation = target.Location;
        }

        private void OnControlMouseMove(object? sender, MouseEventArgs e)
        {
            if (!_dragging || _selected.Count == 0) { return; }
            Control source = (sender as Control) ?? _selectionOverlay!;
            Point current = source.PointToScreen(e.Location);
            int dx = current.X - _dragStart.X;
            int dy = current.Y - _dragStart.Y;
            if (dx != 0 || dy != 0) { _dragMoved = true; }
            _selected[0].Location = new Point(
                _controlStartLocation.X + dx,
                _controlStartLocation.Y + dy);
            UpdateOverlay();
        }

        private void OnControlMouseUp(object? sender, MouseEventArgs e)
        {
            if (!_dragging || _selected.Count == 0)
            {
                _dragging = false;
                return;
            }
            _dragging = false;
            if (_dragMoved)
            {
                foreach (Control c in _selected)
                {
                    SnapToGrid(c);
                    CommitBounds(c);
                }
                UpdateOverlay();
            }
        }

        // -----------------------------------------------------------------
        // Arrow key nudge
        // -----------------------------------------------------------------

        /// <summary>
        /// Mark arrow keys as input keys so WinForms does not swallow
        /// them for control-to-control navigation. This lets the form's
        /// KeyPreview + KeyDown handler capture them for nudge.
        /// </summary>
        private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                e.IsInputKey = true;
            }
        }

        /// <summary>
        /// Form-level KeyDown handler. Arrow keys are captured here
        /// and enqueued as events. The PreviewKeyDown handler already
        /// marked them as input keys so they reach this handler.
        /// </summary>
        private void OnFormKeyDown(object? sender, KeyEventArgs e)
        {
            // No suppression needed -- let the event flow to the
            // control's KeyDown handler where the EventWiringTable
            // will enqueue it for FORMEVENTS.
        }

        // -----------------------------------------------------------------
        // Snap to grid
        // -----------------------------------------------------------------

        /// <summary>
        /// Round-to-nearest snap: position and size are both rounded to the
        /// nearest grid increment using integer division with half-grid bias.
        /// Enforces a minimum size of one grid cell so controls cannot be
        /// snapped down to zero.
        /// </summary>
        private void SnapToGrid(Control c)
        {
            if (_gridSize < 2) { return; }
            int x = ((c.Location.X + _gridSize / 2) / _gridSize) * _gridSize;
            int y = ((c.Location.Y + _gridSize / 2) / _gridSize) * _gridSize;
            int w = ((c.Width + _gridSize / 2) / _gridSize) * _gridSize;
            int h = ((c.Height + _gridSize / 2) / _gridSize) * _gridSize;
            if (w < _gridSize) { w = _gridSize; }
            if (h < _gridSize) { h = _gridSize; }
            c.Location = new Point(x, y);
            c.Size = new Size(w, h);
        }

        // -----------------------------------------------------------------
        // Selection and commit
        // -----------------------------------------------------------------

        /// <summary>
        /// Set or clear the current selection. Updates the overlay position,
        /// writes the selected id into the descriptor's "selected" property
        /// (so FORMGET can read it), and fires the optional callback.
        /// </summary>
        private void Select(Control? target, bool ctrlHeld = false)
        {
            _dragging = false;
            _resizing = false;

            if (target is null)
            {
                // Deselect all
                _selected.Clear();
            }
            else if (ctrlHeld)
            {
                // Ctrl+click: toggle in/out of selection
                if (!_selected.Remove(target))
                {
                    _selected.Add(target);
                }
            }
            else
            {
                // Normal click: replace selection
                _selected.Clear();
                _selected.Add(target);
            }

            UpdateOverlay();
            // Repaint form to clear/draw secondary selection borders
            _form.Invalidate();
            // Report primary selection to the descriptor
            string id = _selected.Count > 0 ? (_selected[0].Name ?? string.Empty) : string.Empty;
            _descriptor.Properties["selected"] = id;
            // Report full multi-selection as comma-separated ids
            if (_selected.Count > 1)
            {
                _descriptor.Properties["selectedall"] = string.Join(" ",
                    _selected.ConvertAll(c => c.Name ?? string.Empty));
            }
            else
            {
                _descriptor.Properties["selectedall"] = id;
            }
            _descriptor.Properties["selectioncount"] = _selected.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            _onSelectionChanged?.Invoke(id);
        }

        /// <summary>
        /// Write the control's current on-screen bounds back to the
        /// <see cref="ControlDescriptor"/> so the descriptor stays in
        /// sync with the visual state after a drag or resize. This is
        /// what makes <c>@FORMSAVE</c> capture the designer's changes.
        /// </summary>
        private void CommitBounds(Control target)
        {
            if (string.IsNullOrEmpty(target.Name)) { return; }
            ControlDescriptor? cd = FindDescriptor(_descriptor, target.Name);
            if (cd is null) { return; }
            // Check if bounds actually changed
            if (cd.X != target.Location.X || cd.Y != target.Location.Y ||
                cd.Width != target.Width || cd.Height != target.Height)
            {
                cd.X = target.Location.X;
                cd.Y = target.Location.Y;
                cd.Width = target.Width;
                cd.Height = target.Height;
                // Signal to the BTM that a move/resize occurred
                // so it can push an undo snapshot and mark dirty.
                _descriptor.Properties["_bounds_changed"] = "1";
            }
        }

        // -----------------------------------------------------------------
        // Descriptor lookup
        // -----------------------------------------------------------------

        private static ControlDescriptor? FindDescriptor(FormDescriptor form, string name)
        {
            return FindDescriptorRecursive(form.Controls, name);
        }

        private static ControlDescriptor? FindDescriptorRecursive(
            List<ControlDescriptor> level, string name)
        {
            foreach (ControlDescriptor c in level)
            {
                if (string.Equals(c.Id, name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
                if (c.Children.Count > 0)
                {
                    ControlDescriptor? nested = FindDescriptorRecursive(c.Children, name);
                    if (nested is not null) { return nested; }
                }
            }
            return null;
        }
    }
}
