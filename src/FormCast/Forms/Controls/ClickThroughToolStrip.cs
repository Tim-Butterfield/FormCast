// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/ClickThroughToolStrip.cs
// =======================================
//
// A ToolStrip subclass that fires button clicks on the FIRST mouse
// click even when the parent form is not the active window. Standard
// WinForms ToolStrip eats the first click to activate the form
// (WM_MOUSEACTIVATE returns MA_ACTIVATEANDEAT). This override
// returns MA_ACTIVATE so the click is passed through to the button.
//
// Used by the designer's toolbar so users can click a toolbar button
// on the Toolbox window immediately after selecting a control on the
// Canvas window without needing to click twice.

using System;
using System.Windows.Forms;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// ToolStrip that passes the activating mouse click through to
    /// its child items instead of eating it.
    /// </summary>
    internal sealed class ClickThroughToolStrip : ToolStrip
    {
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const int MA_ACTIVATE = 1;

        /// <inheritdoc/>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = new IntPtr(MA_ACTIVATE);
                return;
            }
            base.WndProc(ref m);
        }
    }
}
