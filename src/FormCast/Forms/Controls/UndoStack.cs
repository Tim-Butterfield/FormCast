// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/UndoStack.cs
// ===========================
//
// Snapshot-based undo/redo for the FormCast visual designer.
// Each snapshot is a full JSONC serialization of the form
// descriptor, captured before each mutation. Undo restores
// the previous snapshot; redo re-applies the undone state.
//
// The stack is stored per-form (keyed by form handle) and
// managed through @FORMSET props: undo, redo, snapshot.

using System;
using System.Collections.Generic;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// Per-form undo/redo stack. Each entry is a serialized JSONC
    /// string representing the complete form descriptor state at
    /// the time of the snapshot.
    /// </summary>
    internal sealed class UndoStack
    {
        private readonly List<string> _states = new List<string>();
        private int _current = -1;
        private const int MaxDepth = 50;

        /// <summary>True if there is a state to undo to.</summary>
        public bool CanUndo => _current > 0;

        /// <summary>True if there is a state to redo to.</summary>
        public bool CanRedo => _current < _states.Count - 1;

        /// <summary>Number of states in the stack.</summary>
        public int Count => _states.Count;

        /// <summary>
        /// Push a new snapshot. Discards any redo states beyond
        /// the current position (new mutation invalidates redo).
        /// Trims oldest states if the stack exceeds MaxDepth.
        /// </summary>
        public void Push(string jsonSnapshot)
        {
            // Discard redo history
            if (_current < _states.Count - 1)
            {
                _states.RemoveRange(_current + 1, _states.Count - _current - 1);
            }

            _states.Add(jsonSnapshot);
            _current = _states.Count - 1;

            // Trim oldest if over limit
            while (_states.Count > MaxDepth)
            {
                _states.RemoveAt(0);
                _current--;
            }
        }

        /// <summary>
        /// Move back one step. Returns the snapshot to restore,
        /// or null if already at the beginning.
        /// </summary>
        public string? Undo()
        {
            if (!CanUndo) return null;
            _current--;
            return _states[_current];
        }

        /// <summary>
        /// Move forward one step. Returns the snapshot to restore,
        /// or null if already at the end.
        /// </summary>
        public string? Redo()
        {
            if (!CanRedo) return null;
            _current++;
            return _states[_current];
        }

        /// <summary>Clear all history.</summary>
        public void Clear()
        {
            _states.Clear();
            _current = -1;
        }
    }
}
