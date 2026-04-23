// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// A modern on/off toggle switch control. Owner-drawn as a
    /// rounded track with a sliding circular thumb. Fires
    /// <see cref="CheckedChanged"/> on click, compatible with
    /// the same event-binding contract as <see cref="CheckBox"/>.
    /// </summary>
    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;

        /// <summary>
        /// Gets or sets the toggle state.
        /// </summary>
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) { return; }
                _checked = value;
                Invalidate();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Raised when the <see cref="Checked"/> state changes.
        /// </summary>
        public event EventHandler? CheckedChanged;

        /// <summary>
        /// Initialize the toggle switch with owner-draw styles. The four
        /// <see cref="ControlStyles"/> flags eliminate flicker by routing
        /// all painting through <see cref="OnPaint"/> with a
        /// double-buffered back buffer. <see cref="ControlStyles.ResizeRedraw"/>
        /// ensures a repaint on every resize so the rounded track
        /// geometry stays correct.
        /// </summary>
        public ToggleSwitch()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            MinimumSize = new Size(40, 20);
            Size = new Size(50, 26);
            Cursor = Cursors.Hand;
        }

        /// <inheritdoc />
        /// <remarks>
        /// Toggles the checked state on every click. The state change fires
        /// <see cref="CheckedChanged"/> (via the <see cref="Checked"/> setter)
        /// before <c>base.OnClick</c> raises the <see cref="Control.Click"/>
        /// event, so a binding wired to the Click event sees the new state.
        /// </remarks>
        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Clear background to parent color
            using (var bgBrush = new SolidBrush(Parent?.BackColor ?? SystemColors.Control))
            {
                g.FillRectangle(bgBrush, ClientRectangle);
            }

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int pad = 3;
            int thumbD = h - (pad * 2);
            if (thumbD < 4) { thumbD = 4; }

            // Track
            Color trackColor = _checked
                ? Color.FromArgb(0, 120, 212)   // accent blue
                : Color.FromArgb(160, 160, 160); // gray
            int radius = h / 2;
            if (radius < 1) { radius = 1; }
            using (var trackBrush = new SolidBrush(trackColor))
            using (var path = RoundedRect(0, 0, w, h, radius))
            {
                g.FillPath(trackBrush, path);
            }

            // Thumb
            int thumbX = _checked ? w - thumbD - pad : pad;
            using (var thumbBrush = new SolidBrush(Color.White))
            {
                g.FillEllipse(thumbBrush, thumbX, pad, thumbD, thumbD);
            }

            base.OnPaint(e);
        }

        /// <summary>
        /// Build a <see cref="GraphicsPath"/> for a rounded rectangle.
        /// Used to draw the toggle track with pill-shaped ends.
        /// </summary>
        private static GraphicsPath RoundedRect(
            int x, int y, int w, int h, int r)
        {
            var path = new GraphicsPath();
            int d = r * 2;
            if (d > w) { d = w; }
            if (d > h) { d = h; }
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
