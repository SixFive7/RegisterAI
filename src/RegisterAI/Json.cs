// SPDX-FileCopyrightText: 2026 Jori Huisman
// SPDX-License-Identifier: LicenseRef-RegisterAI-FSL-1.1-MIT-5yr

using System.Text.Encodings.Web;
using System.Text.Json;

namespace RegisterAI;

/// <summary>How every JSON document is written: indented, LF, non-ASCII left as UTF-8.</summary>
internal static class Json
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",

        // A path with a non-ASCII letter stays readable. The documents go to a
        // program or a terminal, never into HTML, which is all the stricter
        // default encoder guards against.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes one document to stdout, followed by a line feed.</summary>
    /// <param name="terminal">Where it goes.</param>
    /// <param name="body">What it says.</param>
    public static void Write(Terminal terminal, Action<Utf8JsonWriter> body)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(body);

        using (var writer = new Utf8JsonWriter(terminal.Output, WriterOptions))
        {
            body(writer);
        }

        terminal.WriteText("\n");
    }

    /// <summary>A string, or null.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property.</param>
    /// <param name="value">The value.</param>
    public static void WriteNullableString(this Utf8JsonWriter writer, string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    /// <summary>An array of strings.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="name">The property.</param>
    /// <param name="values">The values.</param>
    public static void WriteStrings(this Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(values);

        writer.WriteStartArray(name);

        foreach (var value in values)
        {
            writer.WriteStringValue(value);
        }

        writer.WriteEndArray();
    }

    /// <summary>The document every verb and usage error starts with.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="verb">The verb, or null when none was recognised.</param>
    public static void WriteHeader(this Utf8JsonWriter writer, string? verb)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteString("tool", Vocabulary.Tool);
        writer.WriteString("version", ToolVersion.Text);
        writer.WriteNumber("schema", Vocabulary.Schema);
        writer.WriteNullableString("verb", verb);
    }
}
