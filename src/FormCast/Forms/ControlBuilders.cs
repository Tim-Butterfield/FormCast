// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;

namespace FormCast.Forms
{
 /// <summary>
 /// Builds <see cref="ControlDescriptor"/> instances from the
 /// arguments TCC passes through <c>@FORMADD</c>. Each recognized
 /// control type has a builder; unknown types are rejected with a
 /// specific error code so the BTM caller knows the failure was
 /// "unknown type" rather than "bad arguments".
 /// </summary>
 /// <remarks>
 /// The builders produce descriptors; the realizer turns each
 /// descriptor into a real WinForms <c>Control</c>. Adding a new
 /// control type means adding a builder here AND a realizer entry;
 /// the descriptor is generic enough to carry any type's data via
 /// the <see cref="ControlDescriptor.Properties"/> bag.
 /// </remarks>
    public static class ControlBuilders
    {
 // Internal hash set for O(1) Contains lookups. Adding a new
 // type means appending here AND adding a realizer entry.
        private static readonly HashSet<string> _recognizedTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "LABEL",
                "EDIT",
                "BUTTON",
                "CHECKBOX",
                "RADIO",
                "PANEL",
                "LISTVIEW",
                "MEMO",
                "RICHMEMO",
                "PROGRESSBAR",
                "GROUPBOX",
                "COMBOBOX",
                "TABCONTROL",
                "TABPAGE",
                "NUMERICUPDOWN",
                "DATETIMEPICKER",
                "LINKLABEL",
                "PICTUREBOX",
                "TRACKBAR",
                "LISTBOX",
                "CHECKEDLISTBOX",
                "MASKEDTEXTBOX",
                "MONTHCALENDAR",
                "TREEVIEW",
                "SPLITCONTAINER",
                "MENUSTRIP",
                "CONTEXTMENU",
                "TOOLBAR",
                "STATUSBAR",
                "DOMAINUPDOWN",
                "HSCROLLBAR",
                "VSCROLLBAR",
                "DATAGRID",
                "FLOWPANEL",
                "TABLEPANEL",
                "PROPERTYGRID",
                "WEBBROWSER",
                "TOGGLE",
                "SEPARATOR",
            };

 /// <summary>
 /// Read-only view of the set of control type tokens this version
 /// of FormCast knows how to build. Comparison via
 /// <see cref="IsRecognizedType"/> is case-insensitive.
 /// </summary>
        public static IReadOnlyCollection<string> RecognizedTypes => _recognizedTypes;

 /// <summary>
 /// Returns <see langword="true"/> if <paramref name="type"/> is
 /// a control type FormCast can build. Case-insensitive. O(1).
 /// </summary>
        public static bool IsRecognizedType(string? type)
        {
            return !string.IsNullOrEmpty(type) && _recognizedTypes.Contains(type!);
        }

 /// <summary>
 /// Build a <see cref="ControlDescriptor"/> using the
 /// absolute-position argument shape from
 /// <c>@FORMADD[handle,ctrlid,type,x,y,w,h[,text]]</c>.
 /// </summary>
 /// <param name="type">Control type token (e.g. "LABEL").</param>
 /// <param name="id">Caller-supplied control identifier.</param>
 /// <param name="x">X coordinate within the parent.</param>
 /// <param name="y">Y coordinate within the parent.</param>
 /// <param name="width">Width in pixels.</param>
 /// <param name="height">Height in pixels.</param>
 /// <param name="text">
 /// Optional caption / initial text. Empty string if the caller
 /// did not supply this argument.
 /// </param>
 /// <returns>
 /// A populated <see cref="ControlDescriptor"/>. Throws
 /// <see cref="ArgumentException"/> if <paramref name="type"/>
 /// is not a recognized control type.
 /// </returns>
        public static ControlDescriptor BuildAbsolute(
            string type,
            string id,
            int x,
            int y,
            int width,
            int height,
            string text)
        {
            if (!IsRecognizedType(type))
            {
                throw new ArgumentException(
                    $"Unknown control type '{type}'. Recognized types: " +
                    string.Join(", ", RecognizedTypes),
                    nameof(type));
            }

            return new ControlDescriptor
            {
                Type = type,
                Id = id,
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Text = text,
            };
        }
    }
}
