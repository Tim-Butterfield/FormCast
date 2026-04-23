// Copyright (c) 2026 Tim Butterfield
// Licensed under the MIT License. See LICENSE file in the repository root.

using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FormCast.Internal
{
 /// <summary>
 /// Indented JSON writer for the FormCast template format. A thin
 /// wrapper over <see cref="Utf8JsonWriter"/>.
 /// The output is RFC 8259 JSON (no comments, no trailing commas);
 /// only the reader needs JSONC. The writer is kept on this
 /// statement-oriented surface so <see cref="Forms.FormSerializer"/>
 /// (which builds the document with explicit
 /// <see cref="BeginObject"/> / <see cref="EndObject"/> calls) does
 /// not need to know about <see cref="Utf8JsonWriter"/>.
 /// </summary>
 /// <remarks>
 /// <see cref="ToString"/> flushes the underlying writer and decodes
 /// the buffered UTF-8 bytes back to a <see cref="string"/>. The
 /// instance is single-use: the recommended pattern is allocate,
 /// drive the structure, call <see cref="ToString"/> once, then
 /// discard.
 /// </remarks>
    internal sealed class JsonWriter
    {
        private static readonly JsonWriterOptions Options =
            new JsonWriterOptions
            {
                Indented = true,
 // The default JavaScriptEncoder.Default escapes
 // characters like '<', '>', '&' that are not strictly
 // required by RFC 8259. Use UnsafeRelaxedJsonEscaping
 // to keep the output to the JSON-mandatory escape set.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

        private readonly MemoryStream _stream = new MemoryStream();
        private readonly Utf8JsonWriter _writer;

        public JsonWriter()
        {
            _writer = new Utf8JsonWriter(_stream, Options);
        }

 /// <summary>Begin a top-level or nested object at the current position.</summary>
        public void BeginObject() => _writer.WriteStartObject();

 /// <summary>Close an object opened with <see cref="BeginObject"/>.</summary>
        public void EndObject() => _writer.WriteEndObject();

 /// <summary>Begin a named array property.</summary>
        public void BeginArray(string name) => _writer.WriteStartArray(name);

 /// <summary>Close an array opened with <see cref="BeginArray"/>.</summary>
        public void EndArray() => _writer.WriteEndArray();

 /// <summary>Begin an unnamed object as an array element.</summary>
        public void BeginArrayElementObject() => _writer.WriteStartObject();

 /// <summary>Write a string-valued property.</summary>
        public void WriteProperty(string name, string value) =>
            _writer.WriteString(name, value ?? string.Empty);

 /// <summary>Write an integer-valued property.</summary>
        public void WriteProperty(string name, int value) =>
            _writer.WriteNumber(name, value);

 /// <summary>
 /// Write a property whose value is a pre-rendered JSON fragment
 /// (object, array, etc.). The fragment must be syntactically
 /// valid JSON; <see cref="Utf8JsonWriter.WriteRawValue(string, bool)"/>
 /// asserts that contract internally. This entry point exists so
 /// callers can splice nested objects without the writer needing
 /// a "begin named object" primitive that complicates the
 /// statement model.
 /// </summary>
        public void WriteRawProperty(string name, string jsonFragment)
        {
            _writer.WritePropertyName(name);
            _writer.WriteRawValue(jsonFragment ?? "null");
        }

 /// <summary>Return the assembled JSON document as a string.</summary>
        public override string ToString()
        {
            _writer.Flush();
            return Encoding.UTF8.GetString(_stream.GetBuffer(), 0, (int)_stream.Length);
        }
    }
}
