// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.
//
// Forms/Controls/DesignPropertyAdapter.cs
// =======================================
//
// Wraps a ControlDescriptor or FormDescriptor so that the WinForms
// PropertyGrid can display and edit all properties natively. Used
// by the visual designer's persistent Properties panel.
//
// The adapter exposes:
// - Well-known strongly-typed properties (Id, Type, X, Y, Width,
//   Height, Text) with appropriate categories
// - Appearance properties (backcolor, forecolor, font, anchor)
// - Event bindings (_bind.click, _bind.change, _bind.close)
// - All other Properties bag entries under a "Misc" category
//
// When the user edits a value in the PropertyGrid, the change is
// written back to the descriptor. The caller (Plugin.cs) is
// responsible for also applying the change to the realized control.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;

namespace FormCast.Forms.Controls
{
    /// <summary>
    /// Adapts a <see cref="ControlDescriptor"/> for display in a
    /// WinForms <see cref="System.Windows.Forms.PropertyGrid"/>.
    /// </summary>
    [TypeConverter(typeof(ExpandableObjectConverter))]
    internal sealed class DesignPropertyAdapter : ICustomTypeDescriptor
    {
        private readonly ControlDescriptor? _control;
        private readonly FormDescriptor? _form;
        private readonly bool _isForm;

        /// <summary>Fires when any property value changes.</summary>
        public event Action<string, string>? PropertyChanged;

        /// <summary>Wrap a control descriptor.</summary>
        public DesignPropertyAdapter(ControlDescriptor control)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _isForm = false;
        }

        /// <summary>Wrap a form descriptor.</summary>
        public DesignPropertyAdapter(FormDescriptor form)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _isForm = true;
        }

        public override string ToString()
        {
            if (_isForm) return _form!.Name + " (Form)";
            return (_control!.Id ?? "") + " (" + (_control.Type ?? "") + ")";
        }

        // -- ICustomTypeDescriptor --
        // These methods satisfy the interface contract. PropertyGrid calls
        // GetProperties() to discover what to display; the other methods
        // delegate to the default TypeDescriptor or return empty/null
        // because we have no custom events, editors, or converters.

        /// <inheritdoc />
        public AttributeCollection GetAttributes() => TypeDescriptor.GetAttributes(this, true);
        public string? GetClassName() => _isForm ? "Form" : _control!.Type;
        public string? GetComponentName() => _isForm ? _form!.Name : _control!.Id;
        public TypeConverter GetConverter() => TypeDescriptor.GetConverter(this, true);
        public EventDescriptor? GetDefaultEvent() => null;
        public PropertyDescriptor? GetDefaultProperty() => null;
        public object? GetEditor(Type editorBaseType) => null;
        public EventDescriptorCollection GetEvents() => EventDescriptorCollection.Empty;
        public EventDescriptorCollection GetEvents(Attribute[] attributes) => EventDescriptorCollection.Empty;

        public PropertyDescriptorCollection GetProperties()
        {
            return GetProperties(Array.Empty<Attribute>());
        }

        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            var props = new List<PropertyDescriptor>();

            if (_isForm)
            {
                BuildFormProperties(props);
            }
            else
            {
                BuildControlProperties(props);
            }

            return new PropertyDescriptorCollection(props.ToArray());
        }

        public object? GetPropertyOwner(PropertyDescriptor? pd) => this;

        // -- Property building --

        /// <summary>
        /// Emit strongly-typed descriptors for the well-known form fields
        /// (Name, Title, layout, position, size) plus the free-form
        /// property bag. Each setter notifies via <see cref="PropertyChanged"/>
        /// so the designer can push the change to the realized form.
        /// </summary>
        private void BuildFormProperties(List<PropertyDescriptor> props)
        {
            FormDescriptor f = _form!;
            props.Add(new DynProp("Name", "Identity", () => f.Name, v => f.Name = v));
            props.Add(new DynProp("Title", "Identity", () => f.Title, v => { f.Title = v; Notify("title", v); }));
            props.Add(new DynProp("Type", "Identity", () => f.Type, v => f.Type = v));
            props.Add(new DynProp("X", "Layout", () => f.X.ToString(CultureInfo.InvariantCulture), v => { f.X = ParseInt(v); Notify("x", v); }));
            props.Add(new DynProp("Y", "Layout", () => f.Y.ToString(CultureInfo.InvariantCulture), v => { f.Y = ParseInt(v); Notify("y", v); }));
            props.Add(new DynProp("Width", "Layout", () => f.Width.ToString(CultureInfo.InvariantCulture), v => { f.Width = ParseInt(v); Notify("width", v); }));
            props.Add(new DynProp("Height", "Layout", () => f.Height.ToString(CultureInfo.InvariantCulture), v => { f.Height = ParseInt(v); Notify("height", v); }));
            props.Add(new DynProp("Layout", "Layout", () => f.LayoutMode, v => { f.LayoutMode = v; Notify("layout", v); }));

            // Bag properties with categories
            AddBagProps(props, f.Properties);
        }

        /// <summary>
        /// Emit strongly-typed descriptors for the well-known control fields
        /// (Id, Type, Text, position, size) plus the free-form property bag.
        /// Type is read-only because changing it would require a full control
        /// rebuild on the realized form.
        /// </summary>
        private void BuildControlProperties(List<PropertyDescriptor> props)
        {
            ControlDescriptor c = _control!;
            props.Add(new DynProp("Id", "Identity", () => c.Id, v => { c.Id = v; Notify("id", v); }));
            props.Add(new DynProp("Type", "Identity", () => c.Type, null, readOnly: true));
            props.Add(new DynProp("Text", "Identity", () => c.Text, v => { c.Text = v; Notify("text", v); }));
            props.Add(new DynProp("X", "Layout", () => c.X.ToString(CultureInfo.InvariantCulture), v => { c.X = ParseInt(v); Notify("position", c.X + ":" + c.Y); }));
            props.Add(new DynProp("Y", "Layout", () => c.Y.ToString(CultureInfo.InvariantCulture), v => { c.Y = ParseInt(v); Notify("position", c.X + ":" + c.Y); }));
            props.Add(new DynProp("Width", "Layout", () => c.Width.ToString(CultureInfo.InvariantCulture), v => { c.Width = ParseInt(v); Notify("size", c.Width + ":" + c.Height); }));
            props.Add(new DynProp("Height", "Layout", () => c.Height.ToString(CultureInfo.InvariantCulture), v => { c.Height = ParseInt(v); Notify("size", c.Width + ":" + c.Height); }));

            // Bag properties with categories
            AddBagProps(props, c.Properties);
        }

        /// <summary>
        /// Emit a <see cref="DynProp"/> for every entry in the property bag,
        /// then ensure commonly-used keys (appearance, events) are always
        /// present even when the bag does not contain them yet. This lets the
        /// designer user add a backcolor or click binding without needing to
        /// pre-populate the bag via <c>@FORMSET</c>.
        /// </summary>
        private void AddBagProps(List<PropertyDescriptor> props, Dictionary<string, string> bag)
        {
            foreach (var kv in bag)
            {
                string key = kv.Key;
                string cat = CategorizeProperty(key);
                // Capture key for closure
                string k = key;
                props.Add(new DynProp(key, cat,
                    () => bag.TryGetValue(k, out string? v) ? v ?? "" : "",
                    v => { bag[k] = v; Notify(k, v); }));
            }

            // Always show these appearance props even if not in bag
            EnsureBagProp(props, bag, "backcolor", "Appearance");
            EnsureBagProp(props, bag, "forecolor", "Appearance");
            EnsureBagProp(props, bag, "font", "Appearance");
            EnsureBagProp(props, bag, "anchor", "Appearance");

            // Show event bindings that match the control type.
            // EventWiringTable knows which events each type supports.
            if (!_isForm && _control is not null)
            {
                Type? winType = MapDescriptorTypeToWinForms(_control.Type);
                if (winType is not null)
                {
                    foreach (string evt in EventWiringTable.GetEventsForType(winType))
                    {
                        EnsureBagProp(props, bag, "_bind." + evt, "Events");
                    }
                }
            }
            else
            {
                // Form-level: only close is meaningful
                EnsureBagProp(props, bag, "_bind.close", "Events");
            }
        }

        private void EnsureBagProp(List<PropertyDescriptor> props, Dictionary<string, string> bag, string key, string category)
        {
            // Only add if not already present (AddBagProps would have added it)
            if (bag.ContainsKey(key)) return;
            string k = key;
            props.Add(new DynProp(key, category,
                () => bag.TryGetValue(k, out string? v) ? v ?? "" : "",
                v => { bag[k] = v; Notify(k, v); }));
        }

        /// <summary>
        /// Map a property bag key to the PropertyGrid category it should
        /// appear under. The categories match WinForms conventions (Identity,
        /// Layout, Appearance, Behavior, Events, Form, Misc) so the grid
        /// groups related properties visually.
        /// </summary>
        private static string CategorizeProperty(string key)
        {
            string lower = key.ToLowerInvariant();
            if (lower.StartsWith("_bind.", StringComparison.Ordinal)) return "Events";
            switch (lower)
            {
                case "backcolor":
                case "forecolor":
                case "font":
                case "anchor":
                case "tooltip":
                    return "Appearance";
                case "checked":
                case "readonly":
                case "multiline":
                case "wordwrap":
                case "mask":
                case "min":
                case "max":
                case "value":
                case "tickfrequency":
                case "style":
                case "selectedindex":
                case "spring":
                case "splitterdistance":
                case "stockicon":
                    return "Behavior";
                case "theme":
                case "darkmode":
                case "design_mode":
                case "showintaskbar":
                case "gridsize":
                    return "Form";
                default:
                    return "Misc";
            }
        }

        private void Notify(string prop, string value)
        {
            PropertyChanged?.Invoke(prop, value);
        }

        /// <summary>
        /// Map a FormCast descriptor type string (e.g. "BUTTON")
        /// to the corresponding WinForms control Type so we can
        /// query EventWiringTable for supported events.
        /// </summary>
        private static Type? MapDescriptorTypeToWinForms(string? type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            switch (type!.ToUpperInvariant())
            {
                case "LABEL": return typeof(System.Windows.Forms.Label);
                case "EDIT": return typeof(System.Windows.Forms.TextBox);
                case "BUTTON": return typeof(System.Windows.Forms.Button);
                case "CHECKBOX": return typeof(System.Windows.Forms.CheckBox);
                case "RADIO": return typeof(System.Windows.Forms.RadioButton);
                case "PANEL": return typeof(System.Windows.Forms.Panel);
                case "GROUPBOX": return typeof(System.Windows.Forms.GroupBox);
                case "LISTVIEW": return typeof(System.Windows.Forms.ListView);
                case "MEMO": return typeof(System.Windows.Forms.TextBox);
                case "PROGRESSBAR": return typeof(System.Windows.Forms.ProgressBar);
                case "COMBOBOX": return typeof(System.Windows.Forms.ComboBox);
                case "TABCONTROL": return typeof(System.Windows.Forms.TabControl);
                case "NUMERICUPDOWN": return typeof(System.Windows.Forms.NumericUpDown);
                case "DATETIMEPICKER": return typeof(System.Windows.Forms.DateTimePicker);
                case "LINKLABEL": return typeof(System.Windows.Forms.LinkLabel);
                case "PICTUREBOX": return typeof(System.Windows.Forms.PictureBox);
                case "TRACKBAR": return typeof(System.Windows.Forms.TrackBar);
                case "LISTBOX": return typeof(System.Windows.Forms.ListBox);
                case "CHECKEDLISTBOX": return typeof(System.Windows.Forms.CheckedListBox);
                case "TREEVIEW": return typeof(System.Windows.Forms.TreeView);
                case "MONTHCALENDAR": return typeof(System.Windows.Forms.MonthCalendar);
                case "SPLITCONTAINER": return typeof(System.Windows.Forms.SplitContainer);
                case "HSCROLLBAR": return typeof(System.Windows.Forms.HScrollBar);
                case "VSCROLLBAR": return typeof(System.Windows.Forms.VScrollBar);
                case "DATAGRID": return typeof(System.Windows.Forms.DataGridView);
                case "TOGGLE": return typeof(ToggleSwitch);
                default: return typeof(System.Windows.Forms.Control);
            }
        }

        private static int ParseInt(string v)
        {
            int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result);
            return result;
        }

        // -- Dynamic PropertyDescriptor --

        /// <summary>
        /// Minimal <see cref="PropertyDescriptor"/> backed by getter/setter
        /// delegates. All values are strings because the FormCast descriptor
        /// model is string-typed (the property bag is
        /// <c>Dictionary&lt;string, string&gt;</c>). PropertyGrid renders
        /// them as editable text fields; richer editing (color picker, font
        /// dialog) is left for future work via custom
        /// <see cref="UITypeEditor"/> instances.
        /// </summary>
        private sealed class DynProp : PropertyDescriptor
        {
            private readonly Func<string> _getter;
            private readonly Action<string>? _setter;
            private readonly string _category;
            private readonly bool _readOnly;

            public DynProp(string name, string category, Func<string> getter, Action<string>? setter, bool readOnly = false)
                : base(name, Array.Empty<Attribute>())
            {
                _getter = getter;
                _setter = setter;
                _category = category;
                _readOnly = readOnly || setter is null;
            }

            public override string Category => _category;
            public override Type ComponentType => typeof(DesignPropertyAdapter);
            public override bool IsReadOnly => _readOnly;
            public override Type PropertyType => typeof(string);

            public override bool CanResetValue(object component) => false;
            public override object? GetValue(object? component) => _getter();
            public override void ResetValue(object component) { }
            public override bool ShouldSerializeValue(object component) => false;

            public override void SetValue(object? component, object? value)
            {
                if (_readOnly) return;
                _setter?.Invoke(value?.ToString() ?? "");
            }
        }
    }
}
