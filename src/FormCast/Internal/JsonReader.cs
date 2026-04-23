// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace FormCast.Internal
{
 /// <summary>
 /// JSONC reader for the FormCast template format. A thin wrapper
 /// over <see cref="JsonDocument"/> that adds JSONC features: line
 /// comments (<c>// ...</c>), block comments (<c>/* ... */</c>),
 /// and trailing commas in objects and arrays.
 /// </summary>
 /// <remarks>
 /// <para>The public API is the static <see cref="Parse"/> entry point
 /// returning a tree of <see cref="Dictionary{TKey, TValue}"/>,
 /// <see cref="List{T}"/>, <see cref="string"/>, <see cref="int"/>,
 /// <see cref="bool"/>, and <see langword="null"/>.</para>
 ///
 /// <para>On parse error throws <see cref="FormatException"/> wrapping
 /// the underlying <see cref="JsonException"/>. The wrapper exists so
 /// callers can catch a single framework-neutral exception type
 /// regardless of which JSON backend is in use.</para>
 ///
 /// <para>Number policy: integer literals produce <see cref="int"/>
 /// values; floating-point or out-of-range integers throw. The
 /// FormCast template format stores only integer coordinates and
 /// sizes, so this is sufficient. Revisit if a control type ever
 /// needs a fractional value.</para>
 /// </remarks>
    internal static class JsonReader
    {
 // Skip line and block comments, accept trailing commas.
 // MaxDepth defaults to 64 in JsonDocumentOptions, which is far
 // beyond any reasonable form template; the deepest current
 // shape is form -> controls[] -> control -> props (depth 4).
        private static readonly JsonDocumentOptions DocumentOptions =
            new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };

 /// <summary>
 /// Parse <paramref name="source"/> and return the root value as
 /// one of: <see cref="Dictionary{TKey, TValue}"/> with string
 /// keys and object values, <see cref="List{T}"/> of object,
 /// <see cref="string"/>, <see cref="int"/>, <see cref="bool"/>,
 /// or <see langword="null"/>. Throws <see cref="FormatException"/>
 /// on syntactically invalid input or on a number that does not
 /// fit in <see cref="int"/>.
 /// </summary>
        public static object? Parse(string source)
        {
            if (source is null) { throw new ArgumentNullException(nameof(source)); }
            try
            {
                using JsonDocument doc = JsonDocument.Parse(source, DocumentOptions);
                return Convert(doc.RootElement);
            }
            catch (JsonException ex)
            {
 // Wrap so callers can catch one framework-neutral type.
 // The position info from JsonException (LineNumber,
 // BytePositionInLine) is exposed in the message.
                throw new FormatException(
                    $"JSON parse error at line {ex.LineNumber + 1}, " +
                    $"col {ex.BytePositionInLine + 1}: {ex.Message}", ex);
            }
        }

 /// <summary>
 /// Recursive walker that converts a <see cref="JsonElement"/>
 /// tree into the legacy boxed-object tree the rest of FormCast
 /// expects. The recursion is bounded by
 /// <see cref="JsonDocumentOptions.MaxDepth"/> (64 by default),
 /// so a malicious template cannot blow the managed stack.
 /// </summary>
        private static object? Convert(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                {
                    var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (JsonProperty p in element.EnumerateObject())
                    {
                        result[p.Name] = Convert(p.Value);
                    }
                    return result;
                }
                case JsonValueKind.Array:
                {
                    var result = new List<object?>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        result.Add(Convert(item));
                    }
                    return result;
                }
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
 // Integer-only number policy: GetInt32 throws
 // FormatException for fractional
 // numbers and OverflowException for out-of-range
 // values; both are caught here and rethrown as
 // FormatException to keep the API stable.
                    if (element.TryGetInt32(out int i)) { return i; }
                    throw new FormatException(
                        $"number '{element.GetRawText()}' is not a valid Int32");
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return null;
                default:
 // JsonValueKind has no other public members; the
 // default arm is here for forward-compat in case
 // a future System.Text.Json version adds one.
                    throw new FormatException(
                        $"unsupported JSON value kind '{element.ValueKind}'");
            }
        }
    }
}
