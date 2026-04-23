// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using FormCast.Internal;

namespace FormCast.Forms
{
 /// <summary>
 /// Round-trip serialization of <see cref="FormDescriptor"/> to and
 /// from the FormCast template format. The format is RFC 8259 JSON;
 /// the reader also accepts JSONC comments and trailing commas while
 /// the writer stays comment-free so saved files keep round-tripping.
 /// </summary>
 /// <remarks>
 /// Layout of a serialized form (well-known fields plus a property
 /// bag for everything else):
 /// <code>
 /// {
 /// "version": 1,
 /// "type": "form",
 /// "name": "settings",
 /// "title": "Settings",
 /// "x": 10, "y": 20, "width": 400, "height": 300,
 /// "layout": "absolute",
 /// "controls": [
 /// {
 /// "type": "BUTTON",
 /// "id": "ok",
 /// "x": 100, "y": 250, "width": 80, "height": 30,
 /// "text": "OK",
 /// "props": { "default": "1" }
 /// }
 /// ]
 /// }
 /// </code>
 /// The runtime handle (e.g. <c>L:1234:7</c>) is intentionally not
 /// part of the serialized form; loading produces a fresh handle so
 /// templates remain reusable across sessions.
 /// </remarks>
    public static class FormSerializer
    {
 /// <summary>Current template schema version. Bumped when the on-disk shape changes.</summary>
        public const int SchemaVersion = 1;

 /// <summary>Serialize a form descriptor to a JSON document.</summary>
        public static string Serialize(FormDescriptor form)
        {
            if (form is null) { throw new ArgumentNullException(nameof(form)); }

            var w = new JsonWriter();
            w.BeginObject();
            w.WriteProperty("version", SchemaVersion);
            w.WriteProperty("type", form.Type);
            w.WriteProperty("name", form.Name);
            w.WriteProperty("title", form.Title);
            w.WriteProperty("x", form.X);
            w.WriteProperty("y", form.Y);
            w.WriteProperty("width", form.Width);
            w.WriteProperty("height", form.Height);
            w.WriteProperty("layout", form.LayoutMode);
 // Form-level property bag (layout config knobs).
 // Emitted only when non-empty so existing templates that
 // do not need it stay byte-identical to earlier serialized forms.
            if (form.Properties.Count > 0)
            {
                WritePropertyBag(w, form.Properties);
            }
            w.BeginArray("controls");
            foreach (ControlDescriptor c in form.Controls)
            {
                WriteControl(w, c);
            }
            w.EndArray();
            w.EndObject();
            return w.ToString();
        }

 /// <summary>Deserialize a JSON document into a new form descriptor.</summary>
        public static FormDescriptor Deserialize(string json) =>
            Deserialize(json, vars: null);

 /// <summary>
 /// Deserialize a JSON document into a new form descriptor with
 /// optional <c>${var}</c> substitution.
 /// </summary>
 /// <param name="json">JSONC source.</param>
 /// <param name="vars">
 /// Optional substitution dictionary. When non-null, every
 /// occurrence of <c>${name}</c> inside any string value of the
 /// parsed template (form-level fields, control fields, property
 /// bag values) is replaced with the corresponding value from
 /// <paramref name="vars"/>. Substitution is single-pass: the
 /// expansion of one variable is not re-scanned for further
 /// placeholders, so a value containing <c>${b}</c> stays
 /// literal. Strict-by-default: an unresolved <c>${name}</c>
 /// reference (when vars is non-null) throws
 /// <see cref="FormatException"/>. Pass <c>null</c> to disable
 /// substitution entirely; in that mode, literal <c>${...}</c>
 /// text in the template is preserved as-is.
 /// </param>
 /// <remarks>
 /// <para>The placeholder grammar is intentionally narrow:
 /// <c>${name}</c> where <c>name</c> matches
 /// <c>[A-Za-z_][A-Za-z0-9_]*</c>. Any <c>$</c> followed by
 /// something else (including the empty <c>${}</c>) is preserved
 /// as a literal. There is no escape syntax; if your template
 /// legitimately needs the literal text <c>${something}</c>, do
 /// not pass a vars dictionary at load time.</para>
 ///
 /// <para>Numeric fields like <c>"width": "${w}"</c> work because
 /// <see cref="ReadInt"/> falls through to
 /// <see cref="int.TryParse(string, NumberStyles, IFormatProvider, out int)"/>
 /// when the deserialized value is a string instead of an int.
 /// This lets one template author site numeric placeholders
 /// inside string literals without needing a "raw text"
 /// substitution pass that could break the surrounding JSON.</para>
 /// </remarks>
        public static FormDescriptor Deserialize(string json, IDictionary<string, string>? vars)
        {
            if (json is null) { throw new ArgumentNullException(nameof(json)); }

            object? root = JsonReader.Parse(json);
            if (root is not Dictionary<string, object?> obj)
            {
                throw new FormatException("FormCast template root must be an object");
            }

 // Substitute ${var} placeholders in every string value of
 // the parsed tree. Done after parse so the substituted
 // values cannot break the JSON syntax.
            if (vars is not null)
            {
                SubstituteInPlace(obj, vars);
            }

            var form = new FormDescriptor
            {
                Type = ReadString(obj, "type", "form"),
                Name = ReadString(obj, "name", string.Empty),
                Title = ReadString(obj, "title", string.Empty),
                X = ReadInt(obj, "x", 0),
                Y = ReadInt(obj, "y", 0),
                Width = ReadInt(obj, "width", 0),
                Height = ReadInt(obj, "height", 0),
                LayoutMode = ReadString(obj, "layout", "absolute"),
            };

 // Form-level property bag (layout config knobs).
            if (obj.TryGetValue("props", out object? formPropsRaw) &&
                formPropsRaw is Dictionary<string, object?> formPropBag)
            {
                foreach (KeyValuePair<string, object?> kv in formPropBag)
                {
                    form.Properties[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                }
            }

            if (obj.TryGetValue("controls", out object? controlsRaw) &&
                controlsRaw is List<object?> controls)
            {
                foreach (object? item in controls)
                {
                    if (item is not Dictionary<string, object?> co) { continue; }
                    form.Controls.Add(ReadControl(co));
                }
            }

            return form;
        }

 /// <summary>
 /// Parse a single control object, recursing into a
 /// <c>"children"</c> array if present so PANEL nests round-trip.
 /// </summary>
        private static ControlDescriptor ReadControl(Dictionary<string, object?> co)
        {
            var control = new ControlDescriptor
            {
                Type = ReadString(co, "type", string.Empty),
                Id = ReadString(co, "id", string.Empty),
                X = ReadInt(co, "x", 0),
                Y = ReadInt(co, "y", 0),
                Width = ReadInt(co, "width", 0),
                Height = ReadInt(co, "height", 0),
                Text = ReadString(co, "text", string.Empty),
            };
            if (co.TryGetValue("props", out object? propsRaw) &&
                propsRaw is Dictionary<string, object?> propBag)
            {
                foreach (KeyValuePair<string, object?> kv in propBag)
                {
                    control.Properties[kv.Key] = kv.Value?.ToString() ?? string.Empty;
                }
            }
            if (co.TryGetValue("children", out object? childrenRaw) &&
                childrenRaw is List<object?> childList)
            {
                foreach (object? child in childList)
                {
                    if (child is Dictionary<string, object?> childObj)
                    {
                        control.Children.Add(ReadControl(childObj));
                    }
                }
            }
            return control;
        }

 /// <summary>
 /// Write a single control as a JSON object on the current
 /// array. Recurses into <see cref="ControlDescriptor.Children"/>
 /// when present so PANEL nests round-trip.
 /// </summary>
        private static void WriteControl(JsonWriter w, ControlDescriptor c)
        {
            w.BeginArrayElementObject();
            w.WriteProperty("type", c.Type);
            w.WriteProperty("id", c.Id);
            w.WriteProperty("x", c.X);
            w.WriteProperty("y", c.Y);
            w.WriteProperty("width", c.Width);
            w.WriteProperty("height", c.Height);
            w.WriteProperty("text", c.Text);
            if (c.Properties.Count > 0)
            {
                WritePropertyBag(w, c.Properties);
            }
            if (c.Children.Count > 0)
            {
                w.BeginArray("children");
                foreach (ControlDescriptor child in c.Children)
                {
                    WriteControl(w, child);
                }
                w.EndArray();
            }
            w.EndObject();
        }

 // The JsonWriter API only exposes BeginArray for named arrays
 // and BeginObject for unnamed objects. The property bag needs
 // a *named* nested object, which we synthesize by writing a
 // small inline JSON fragment via the same writer's escape rules.
        private static void WritePropertyBag(JsonWriter w, Dictionary<string, string> bag)
        {
 // Use a fresh inner writer to render the bag, then splice it
 // back in as a property by reusing WriteProperty. This is the
 // tradeoff that comes from keeping JsonWriter strictly
 // statement-oriented (no "begin named object" primitive).
 // Reach via a tiny serialization helper:
            var inner = new JsonWriter();
            inner.BeginObject();
            foreach (KeyValuePair<string, string> kv in bag)
            {
                inner.WriteProperty(kv.Key, kv.Value ?? string.Empty);
            }
            inner.EndObject();
 // Re-emit as raw JSON via a dedicated writer entry point.
            w.WriteRawProperty("props", inner.ToString());
        }

        private static string ReadString(Dictionary<string, object?> obj, string key, string fallback)
        {
            if (obj.TryGetValue(key, out object? raw) && raw is string s)
            {
                return s;
            }
            return fallback;
        }

        private static int ReadInt(Dictionary<string, object?> obj, string key, int fallback)
        {
            if (!obj.TryGetValue(key, out object? raw)) { return fallback; }
            if (raw is int i) { return i; }
 // Accept a string-typed numeric (e.g. "width": "${w}"
 // expanded to "400") and parse it. Falls through to the
 // default if the parse fails.
            if (raw is string s &&
                int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }
            return fallback;
        }

 // -----------------------------------------------------------------
 // ${var} substitution
 // -----------------------------------------------------------------

 // Match ${name} where name = [A-Za-z_][A-Za-z0-9_]*. The leading
 // class is restrictive on purpose: a $ followed by anything
 // else (including ${}, ${1foo}, $bar) is preserved literally
 // by Substitute.
        private static readonly Regex PlaceholderRegex =
            new Regex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

 /// <summary>
 /// Walk a parsed JSON tree (Dict / List / string / int / bool /
 /// null) in place, replacing every <c>${name}</c> placeholder in
 /// each string value with the corresponding value from
 /// <paramref name="vars"/>. Substitution is single-pass; the
 /// expansion of one variable is not re-scanned. An unresolved
 /// reference throws <see cref="FormatException"/>.
 /// </summary>
        private static void SubstituteInPlace(
            Dictionary<string, object?> obj,
            IDictionary<string, string> vars)
        {
 // Snapshot the keys: we are going to assign new string
 // values back into the dictionary, which is allowed under
 // a snapshotted iteration.
            var keys = new List<string>(obj.Keys);
            foreach (string key in keys)
            {
                obj[key] = SubstituteValue(obj[key], vars);
            }
        }

        private static object? SubstituteValue(object? value, IDictionary<string, string> vars)
        {
            switch (value)
            {
                case string s:
                    return Substitute(s, vars);
                case Dictionary<string, object?> nested:
                    SubstituteInPlace(nested, vars);
                    return nested;
                case List<object?> list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        list[i] = SubstituteValue(list[i], vars);
                    }
                    return list;
                default:
 // ints, bools, null: pass through unchanged.
                    return value;
            }
        }

        private static string Substitute(string input, IDictionary<string, string> vars)
        {
            if (string.IsNullOrEmpty(input) || input.IndexOf('$') < 0)
            {
                return input;
            }

 // Build the result manually so we can throw a useful
 // FormatException on the FIRST unresolved reference,
 // rather than letting Regex.Replace silently substitute
 // empty for missing keys.
            var sb = new StringBuilder(input.Length);
            int pos = 0;
            foreach (Match m in PlaceholderRegex.Matches(input))
            {
                if (m.Index > pos)
                {
                    sb.Append(input, pos, m.Index - pos);
                }
                string name = m.Groups[1].Value;
                if (!vars.TryGetValue(name, out string? replacement))
                {
                    throw new FormatException(
                        $"unresolved template variable '${{{name}}}'");
                }
                sb.Append(replacement);
                pos = m.Index + m.Length;
            }
            if (pos < input.Length)
            {
                sb.Append(input, pos, input.Length - pos);
            }
            return sb.ToString();
        }

 /// <summary>
 /// Parse a BTM-side <c>key=value|key=value|...</c> variable
 /// string into a <see cref="Dictionary{TKey, TValue}"/>. Empty
 /// or null input returns an empty dictionary. Pipe characters
 /// are the segment separator (matching the design doc's
 /// <c>@FORMLOAD[file,vars]</c> shape, which TCC users escape
 /// in BTM with <c>^|</c>). The first <c>=</c> in each segment
 /// separates key from value; subsequent <c>=</c> characters
 /// are part of the value. Segments without a <c>=</c> are
 /// treated as <c>key=</c> (empty value). Whitespace around
 /// the key is trimmed; the value is preserved verbatim.
 /// </summary>
        public static Dictionary<string, string> ParseVars(string? varsString)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(varsString)) { return result; }

            string[] segments = varsString!.Split('|');
            foreach (string segment in segments)
            {
                if (segment.Length == 0) { continue; }
                int eq = segment.IndexOf('=');
                if (eq < 0)
                {
                    string key = segment.Trim();
                    if (key.Length > 0) { result[key] = string.Empty; }
                    continue;
                }
                string k = segment.Substring(0, eq).Trim();
                string v = segment.Substring(eq + 1);
                if (k.Length > 0) { result[k] = v; }
            }
            return result;
        }
    }
}
